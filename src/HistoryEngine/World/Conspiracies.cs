using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// Persistent political plots: who wanted a ruler gone, who joined them, and what became of it.
/// </summary>
/// <remarks>
/// <para><b>A plot is a record, not a roll.</b> The old model asked each court once a year whether
/// its ruler was murdered and jumped straight from a motive to a corpse. Here somebody must first
/// have a grounded reason — a succession lost, an office taken, a murdered relative, a quarrel rank
/// forbade them to answer — then find people willing to join them, then find a way to the target,
/// and survive every year in which a court can notice. Most plots never reach an attempt, and the
/// ones that do have a decade of record behind them.</para>
///
/// <para><b>Recruitment is tested against something real.</b> A candidate joins because they owe
/// the leader, trust the leader, hold their own grievance against the target, or want the throne
/// themselves. Where no such tie exists nobody is recruited; there is deliberately no fallback that
/// picks a courtier because one was needed. A household member's access may also be used without
/// their knowing what it was for — recorded, but not as belief.</para>
///
/// <para><b>The engine keeps the truth; the chronicle keeps what got out.</b> Nothing is written to
/// the timeline while a plot is secret, so an abandoned plot leaves no event at all and a reader of
/// the year sees what a contemporary saw. <see cref="FigurePlot.PublicYear"/> is the year the world
/// learned of it, and the export carries both the year each act happened and whether it was known
/// then.</para>
///
/// <para>Endings reuse what already exists: <see cref="Houses.Die"/> for a murder,
/// <see cref="EventKind.RulerDeposed"/> and the ordinary succession for a deposition,
/// <see cref="Figure.AccusedYear"/> for the scaffold the incident system may later bring, and
/// <see cref="Disputes"/> for the quarrels an exposure or a bereavement opens.</para>
/// </remarks>
public static class Conspiracies
{
    /// <summary>Years after a wrong during which it can still start a plot.</summary>
    private const int GrudgeYears = 8;

    /// <summary>Years without any progress after which a plot is treated as having died out.</summary>
    private const int StaleYears = 10;

    /// <summary>
    /// Years after one plot ends before the same person may begin another.
    /// </summary>
    /// <remarks>
    /// A man whose conspiracy was exposed last year is watched, not free to start the next one, and
    /// the plot he just abandoned is the reason he abandoned it. It also keeps one person's plots
    /// from overlapping in the record, so a year can be read against exactly one of them.
    /// </remarks>
    private const int CooldownYears = 6;

    /// <summary>How much of a grievance a plot needs before it is worth the risk.</summary>
    private const double GrievanceFloor = 0.30;

    /// <summary>The most people a plot may draw in, the unwitting one included.</summary>
    private const int MaxMembers = 3;

    /// <summary>What a candidate's motive must reach before they would risk their neck.</summary>
    private const double MotiveFloor = 0.30;

    /// <summary>Clandestine steps a plot must take before it can be attempted at all.</summary>
    private const int RequiredSteps = 2;

    /// <summary>The grounded wrong a plot began from, and where the record of it is.</summary>
    private readonly record struct Grounds(
        PlotCause Cause, EventKind SourceKind, EntityId SourceEntityId);

    // -----------------------------------------------------------------------
    // The yearly pass
    // -----------------------------------------------------------------------

    /// <summary>Carries every open plot one year further, then offers new ones a beginning.</summary>
    public static void Tick(WorldState world, int year)
    {
        // Gathered before anything advances, because an ending can reach Houses.Die and from there
        // into systems that add plots and quarrels. Collecting first means a death this year can
        // never mutate a list this loop is standing in.
        var open = new List<FigurePlot>();
        foreach (Figure figure in world.Figures)
        {
            foreach (FigurePlot plot in figure.Plots)
            {
                if (plot.IsOpen && plot.LeaderId == figure.Id) open.Add(plot);
            }
        }

        foreach (FigurePlot plot in open)
        {
            if (!plot.IsOpen) continue;
            Advance(world, plot, year);
        }

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Consider(world, civilization, year);
        }
    }

    /// <summary>
    /// Closes every plot a person's death leaves without a leader or without a target.
    /// </summary>
    /// <remarks>
    /// Called from the death itself rather than left to next year's pass, so no export can show a
    /// live plot with a dead man in it — including a death in the final year of a run, which next
    /// year's pass would never see.
    /// </remarks>
    public static void EndAtDeath(WorldState world, Figure figure, int year)
    {
        Close(
            world,
            figure,
            year,
            EventKind.FigureDied,
            "death ended it before it was attempted",
            leaders: true);
    }

    /// <summary>
    /// Closes every plot against a ruler who has left the throne some other way.
    /// </summary>
    /// <remarks>
    /// A term ending, an abdication, a rising, or a realm falling out from under them. The
    /// objective is the throne, so it stops being possible the moment the throne is vacated —
    /// and, like a death, it must be settled where the damage happens: the annual pass cannot see
    /// a throne vacated after it has run, and in a final year it never sees it at all.
    /// </remarks>
    public static void EndAtLossOfThrone(WorldState world, Figure ruler, int year)
    {
        Close(
            world,
            ruler,
            year,
            EventKind.RulerTermEnded,
            "its target had left the throne",
            leaders: false);
    }

    /// <summary>Closes the open plots one person's change of fortune makes impossible.</summary>
    /// <param name="leaders">Whether plots this person leads end too, or only plots against them.</param>
    private static void Close(
        WorldState world, Figure figure, int year, EventKind kind, string how, bool leaders)
    {
        foreach (Figure other in world.Figures)
        {
            foreach (FigurePlot plot in other.Plots)
            {
                if (!plot.IsOpen || plot.LeaderId != other.Id) continue;
                if (plot.TargetId != figure.Id && !(leaders && plot.LeaderId == figure.Id)) continue;

                Close(
                    world,
                    plot,
                    year,
                    PlotOutcome.Abandoned,
                    kind,
                    plot.LeaderId == figure.Id && leaders
                        ? "its leader died with it unattempted"
                        : how,
                    reveal: false);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Beginning
    // -----------------------------------------------------------------------

    /// <summary>
    /// Offers one realm's best-grounded enemy of its ruler the chance to become a plot.
    /// </summary>
    /// <remarks>
    /// The candidate is chosen deterministically from what the record already says — a cause, a
    /// motive and a route in — and only then is a roll made, forked on the two people and the year.
    /// A realm with nobody who can name a wrong produces no plot however many years pass.
    /// </remarks>
    private static void Consider(WorldState world, Civilization civilization, int year)
    {
        if (!world.Figures.Contains(civilization.CurrentRulerId)) return;

        Figure target = world.Figures[civilization.CurrentRulerId];
        if (!target.IsAlive || target.AgeIn(year) < Succession.MajorityAge) return;
        if (AnyPlotAgainst(world, target)) return;

        var claimants = new HashSet<EntityId>();
        foreach (Figure claimant in Succession.Claimants(
            world, civilization, world.CultureOf(civilization), EntityId.None))
        {
            claimants.Add(claimant.Id);
        }

        Figure? best = null;
        Grounds bestGrounds = default;
        double bestScore = 0.0;
        double bestAccess = 0.0;

        foreach (Figure candidate in world.Figures)
        {
            if (!candidate.IsAlive || candidate.Id == target.Id) continue;
            if (candidate.CivilizationId != civilization.Id) continue;
            if (candidate.AgeIn(year) < Succession.MajorityAge) continue;
            if (!Available(candidate, year)) continue;

            bool claimant = claimants.Contains(candidate.Id);
            if (CauseFor(world, candidate, target, claimant, year) is not Grounds grounds) continue;

            double motive = Motive(candidate, target, claimant, year);
            if (motive < MotiveFloor) continue;

            double access = Access(world, candidate, target);
            double score = (motive * 0.72) + (access * 0.28);

            if (best is null
                || score > bestScore
                || (score == bestScore && candidate.Id.CompareTo(best.Id) < 0))
            {
                best = candidate;
                bestGrounds = grounds;
                bestScore = score;
                bestAccess = access;
            }
        }

        if (best is null) return;

        if (!Fork(world, best, target, year, "begin").Chance(0.060 + (0.180 * bestScore))) return;

        Begin(
            world,
            civilization,
            best,
            target,
            bestGrounds,
            bestAccess,
            claimants.Contains(best.Id),
            year);
    }

    /// <summary>
    /// What this person can point at when asked why. Null means there is no plot to be had.
    /// </summary>
    /// <remarks>
    /// Ordered by how sharp the wrong is rather than by how likely it is. A quarrel that rank left
    /// unanswerable is the most specific thing this world can say about why somebody went from
    /// hating a ruler to conspiring against one, so it is asked first.
    /// </remarks>
    private static Grounds? CauseFor(
        WorldState world, Figure candidate, Figure target, bool claimant, int year)
    {
        FigureBond? bond = LifeStories.BondTo(candidate, target.Id);
        double grievance = bond?.Grievance ?? 0.0;

        foreach (FigureDispute dispute in candidate.Disputes)
        {
            if (dispute.Other(candidate.Id) != target.Id) continue;

            // Answered quarrels are answered, however they ended. What sends anger here is the
            // quarrel that could not be carried to satisfaction because of who the other man is.
            if (dispute.Outcome is not (DisputeOutcome.Open or DisputeOutcome.Lapsed)) continue;
            if (grievance < GrievanceFloor) continue;

            return new Grounds(PlotCause.QuarrelBeyondReach, EventKind.DisputeOpened, target.Id);
        }

        if (candidate.DisgracedYear is int disgraced
            && year - disgraced <= GrudgeYears
            && grievance >= GrievanceFloor)
        {
            return new Grounds(PlotCause.OfficeRevoked, EventKind.OfficeRevoked, target.Id);
        }

        if (candidate.KinMurderedYear is int murdered
            && year - murdered <= GrudgeYears
            && grievance >= GrievanceFloor)
        {
            return new Grounds(PlotCause.KinMurdered, EventKind.FigureDied, target.Id);
        }

        if (claimant && grievance >= GrievanceFloor)
        {
            return new Grounds(
                PlotCause.SuccessionPassedOver, EventKind.SuccessionDisputed, target.Id);
        }

        return null;
    }

    private static void Begin(
        WorldState world,
        Civilization civilization,
        Figure leader,
        Figure target,
        Grounds grounds,
        double access,
        bool claimant,
        int year)
    {
        // A claimant has somewhere to put the throne afterwards, and unseating a man he can lawfully
        // succeed is worth more to him than killing one. Everyone else has no use for a vacant seat
        // they cannot fill, so what they want is the man gone rather than the office empty.
        PlotObjective objective =
            claimant && Fork(world, leader, target, year, "objective").Chance(0.60)
                ? PlotObjective.Depose
                : PlotObjective.Assassinate;

        var plot = new FigurePlot(
            LedBy(leader),
            leader.Id,
            target.Id,
            civilization.Id,
            objective,
            year,
            grounds.Cause,
            grounds.SourceKind,
            grounds.SourceEntityId,
            world.ResidenceOf(target),
            // Two clandestine steps for either objective: a way in, and a moment to use it. What
            // separates a coup from a murder is not how long the approach takes but how many
            // people it needs — see the backing gate and the attempt modifier below.
            RequiredSteps)
        {
            Secrecy = 0.86,
            Access = DetMath.Clamp01(access),
        };

        plot.Acts.Add(new PlotAct(
            year,
            grounds.SourceKind,
            PlotPhase.Gathering,
            leader.Id,
            "resolved on " + ObjectiveDetail(objective) + ", over " + CauseDetail(grounds.Cause),
            Known: false));

        leader.Plots.Add(plot);
        LifeStories.Remember(
            leader,
            MemoryKind.Conspiracy,
            year,
            grounds.SourceKind,
            target.Id,
            plot.PlaceId,
            0.78);
    }

    // -----------------------------------------------------------------------
    // The years in between
    // -----------------------------------------------------------------------

    private static void Advance(WorldState world, FigurePlot plot, int year)
    {
        Figure leader = world.Figures[plot.LeaderId];
        if (!world.Figures.Contains(plot.TargetId)) return;

        Figure target = world.Figures[plot.TargetId];
        if (Lapsed(world, plot, leader, target, year)) return;

        Recruit(world, plot, leader, target, year);

        // Secrecy is spent by having people in it and by the years passing with a court watching.
        plot.Secrecy = DetMath.Clamp01(plot.Secrecy - 0.02 - (0.015 * plot.WittingCount));
        plot.Suspicion = DetMath.Clamp01(
            plot.Suspicion
            + ((1.0 - plot.Secrecy) * 0.16)
            + (Vigilance(world, plot, target) * 0.06));

        if (Betrayed(world, plot, leader, target, year)) return;

        if (Fork(world, leader, target, year, "discovery").Chance(
            DetMath.Clamp(0.02 + (plot.Suspicion * 0.30), 0.02, 0.45))
            && plot.Suspicion > 0.20)
        {
            Expose(world, plot, leader, target, year, EntityId.None);
            return;
        }

        if (plot.Phase == PlotPhase.Gathering)
        {
            bool enough = plot.WittingCount >= (plot.Objective == PlotObjective.Depose ? 2 : 1)
                || plot.Access >= 0.60;
            if (!enough) return;

            plot.Phase = PlotPhase.Access;
            plot.LastActionYear = year;
            plot.Acts.Add(new PlotAct(
                year,
                EventKind.UndertakingStarted,
                PlotPhase.Access,
                leader.Id,
                "began looking for a way to the " + (plot.Objective == PlotObjective.Depose
                    ? "throne"
                    : "person"),
                Known: false));
            return;
        }

        double momentum = DetMath.Clamp01(
            0.18 + (plot.Access * 0.34) + (plot.WittingCount * 0.08) - (plot.Suspicion * 0.12));
        if (Fork(world, leader, target, year, "advance").Chance(momentum))
        {
            plot.Progress++;
            plot.LastActionYear = year;
            plot.Access = DetMath.Clamp01(plot.Access + 0.06);
            plot.Acts.Add(new PlotAct(
                year,
                EventKind.UndertakingStarted,
                plot.Phase,
                leader.Id,
                "moved a step closer without being seen",
                Known: false));
        }

        if (plot.Progress < plot.RequiredProgress) return;

        plot.Phase = PlotPhase.Ready;
        Attempt(world, plot, leader, target, year);
    }

    /// <summary>Every way a plot can stop being possible before anybody acts on it.</summary>
    private static bool Lapsed(
        WorldState world, FigurePlot plot, Figure leader, Figure target, int year)
    {
        if (!leader.IsAlive || !target.IsAlive)
        {
            Close(
                world,
                plot,
                year,
                PlotOutcome.Abandoned,
                EventKind.FigureDied,
                "death ended it before it was attempted",
                reveal: false);
            return true;
        }

        if (!world.Civilizations.Contains(plot.RealmId)
            || !world.Civilizations[plot.RealmId].IsActive)
        {
            Close(
                world,
                plot,
                year,
                PlotOutcome.Abandoned,
                EventKind.CivilizationFell,
                "the realm it was aimed at was gone",
                reveal: false);
            return true;
        }

        Civilization realm = world.Civilizations[plot.RealmId];
        if (realm.CurrentRulerId != target.Id)
        {
            Close(
                world,
                plot,
                year,
                PlotOutcome.Abandoned,
                EventKind.RulerCrowned,
                realm.CurrentRulerId == leader.Id
                    ? "its leader came to the throne without it"
                    : "its target had already left the throne",
                reveal: false);
            return true;
        }

        if (leader.CivilizationId != plot.RealmId)
        {
            Close(
                world,
                plot,
                year,
                PlotOutcome.Abandoned,
                EventKind.JourneyMade,
                "its leader was no longer in the realm",
                reveal: false);
            return true;
        }

        if (year - plot.LastActionYear >= StaleYears)
        {
            Close(
                world,
                plot,
                year,
                PlotOutcome.Abandoned,
                EventKind.UndertakingFailed,
                "it came to nothing over the years",
                reveal: false);
            return true;
        }

        // The anger it was built on can simply go. A plot kept alive by a grievance that has
        // faded is a man conspiring out of habit, which is not what any of this is modelling.
        if (Motive(leader, target, claimant: false, year) < 0.18
            && Fork(world, leader, target, year, "cool").Chance(0.45))
        {
            Close(
                world,
                plot,
                year,
                PlotOutcome.Abandoned,
                EventKind.UndertakingFailed,
                "the anger behind it cooled",
                reveal: false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Offers the plot one more person, if anyone in the realm has a reason to be that person.
    /// </summary>
    /// <remarks>
    /// The tie is decided before the roll and recorded on the member. Where nobody in the realm has
    /// one, nobody joins — a plot of one is a legitimate plot and a far better history than a plot
    /// of three courtiers picked because three were wanted.
    /// </remarks>
    private static void Recruit(
        WorldState world, FigurePlot plot, Figure leader, Figure target, int year)
    {
        if (plot.Members.Count >= MaxMembers) return;
        if (!Fork(world, leader, target, year, "recruit").Chance(0.50)) return;

        Figure? best = null;
        PlotTie bestTie = PlotTie.TrustInLeader;
        bool bestWitting = true;
        double bestScore = 0.0;

        var claimants = new HashSet<EntityId>();
        if (world.Civilizations.Contains(plot.RealmId))
        {
            Civilization realm = world.Civilizations[plot.RealmId];
            foreach (Figure claimant in Succession.Claimants(
                world, realm, world.CultureOf(realm), EntityId.None))
            {
                claimants.Add(claimant.Id);
            }
        }

        foreach (Figure candidate in world.Figures)
        {
            if (!candidate.IsAlive || candidate.Id == leader.Id || candidate.Id == target.Id) continue;
            if (candidate.CivilizationId != plot.RealmId) continue;
            if (candidate.AgeIn(year) < Succession.MajorityAge) continue;
            if (plot.HasMember(candidate.Id) || Leads(candidate)) continue;

            if (TieFor(world, plot, candidate, leader, target, claimants) is not
                (PlotTie tie, bool witting, double score))
            {
                continue;
            }

            if (best is null
                || score > bestScore
                || (score == bestScore && candidate.Id.CompareTo(best.Id) < 0))
            {
                best = candidate;
                bestTie = tie;
                bestWitting = witting;
                bestScore = score;
            }
        }

        if (best is null) return;

        plot.Members.Add(new PlotMember(best.Id, year, bestTie, bestWitting));
        plot.Access = Math.Max(plot.Access, Access(world, best, target));
        plot.LastActionYear = year;
        plot.Acts.Add(new PlotAct(
            year,
            EventKind.ConspiratorJoined,
            plot.Phase,
            best.Id,
            bestWitting ? "joined it, " + TieDetail(bestTie) : "was used for their access, unknowing",
            Known: false));

        if (!bestWitting) return;

        // Only the witting carry the plot on their own page, and only they take the bond it
        // creates. A man whose kitchen was borrowed has not become a conspirator.
        best.Plots.Add(plot);
        plot.Secrecy = DetMath.Clamp01(plot.Secrecy - 0.06);
        LifeStories.AddConspirators(leader, best, year);
        LifeStories.Remember(
            best, MemoryKind.Conspiracy, year, EventKind.ConspiratorJoined, target.Id, plot.PlaceId, 0.62);
    }

    /// <summary>
    /// What would bind this person to this plot, if anything would.
    /// </summary>
    /// <remarks>
    /// Every branch reads something the world wrote: an obligation, a trust, a grievance, or a
    /// claim on the same throne. The last branch is the unwitting one, which asks only for access
    /// and a reason for the leader to be near them, and never asks the person to believe anything.
    /// </remarks>
    private static (PlotTie Tie, bool Witting, double Score)? TieFor(
        WorldState world,
        FigurePlot plot,
        Figure candidate,
        Figure leader,
        Figure target,
        HashSet<EntityId> claimants)
    {
        FigureBond? toLeader = LifeStories.BondTo(candidate, leader.Id);
        FigureBond? toTarget = LifeStories.BondTo(candidate, target.Id);
        double access = Access(world, candidate, target);
        double grievance = toTarget?.Grievance ?? 0.0;
        double trust = toLeader?.Trust ?? 0.0;
        double obligation = toLeader?.Obligation ?? 0.0;

        // Loyalty to the target beats every reason to join. Someone who owes the ruler is the
        // person a plot must not recruit, and the reason plots stay small.
        if ((toTarget?.Obligation ?? 0.0) >= 0.40) return null;

        if (grievance >= GrievanceFloor)
        {
            return (PlotTie.GrievanceAgainstTarget, true, (grievance * 0.60) + (access * 0.40));
        }

        if (obligation >= 0.25)
        {
            return (PlotTie.ObligationToLeader, true, (obligation * 0.55) + (access * 0.45));
        }

        if (trust >= 0.25)
        {
            return (PlotTie.TrustInLeader, true, (trust * 0.50) + (access * 0.50));
        }

        if (claimants.Contains(candidate.Id) && candidate.Disposition.Independence >= 0.50)
        {
            return (PlotTie.Ambition, true, 0.35 + (access * 0.40));
        }

        // The unwitting route, and the only one that adds access without adding a believer. It
        // still needs a real relationship to the leader — not warmth, but a reason to be standing
        // near them — because access nobody can reach is access the plot does not have.
        if (plot.Access < 0.55
            && access >= 0.40
            && toLeader is not null
            && trust >= 0.0
            && !plot.Members.Exists(member => !member.Witting))
        {
            return (PlotTie.Household, false, access * 0.45);
        }

        return null;
    }

    /// <summary>One of its own decides the risk is worse than the cause.</summary>
    private static bool Betrayed(
        WorldState world, FigurePlot plot, Figure leader, Figure target, int year)
    {
        foreach (PlotMember member in plot.Members)
        {
            if (!member.Witting) continue;
            if (!world.Figures.Contains(member.FigureId)) continue;

            Figure conspirator = world.Figures[member.FigureId];
            if (!conspirator.IsAlive) continue;

            FigureBond? toLeader = LifeStories.BondTo(conspirator, leader.Id);
            FigureBond? toTarget = LifeStories.BondTo(conspirator, target.Id);
            double nerve = (toLeader?.Trust ?? 0.0) + (toLeader?.Obligation ?? 0.0);
            double pull = (toTarget?.Fear ?? 0.0) + (toTarget?.Obligation ?? 0.0)
                + LifeStories.Feelings(conspirator, year).Fear;

            double chance = DetMath.Clamp(0.030 + (pull * 0.12) - (nerve * 0.06), 0.0, 0.25);
            if (chance <= 0.0) continue;
            if (!Fork(world, conspirator, leader, year, "betray").Chance(chance)) continue;

            Expose(world, plot, leader, target, year, conspirator.Id);
            return true;
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Endings
    // -----------------------------------------------------------------------

    /// <summary>The court learns of it, either by watching or by being told.</summary>
    private static void Expose(
        WorldState world,
        FigurePlot plot,
        Figure leader,
        Figure target,
        int year,
        EntityId informer)
    {
        bool betrayed = !informer.IsNone;
        plot.BetrayerId = informer;
        Close(
            world,
            plot,
            year,
            betrayed ? PlotOutcome.Betrayed : PlotOutcome.Exposed,
            EventKind.ConspiracyExposed,
            betrayed ? "one of its own gave it up" : "the court found it",
            reveal: true);

        Accuse(world, plot, leader, target, year);

        LifeStories.AddRivalry(
            leader, target, year, EventKind.ConspiracyExposed, plot.PlaceId, 0.72);
        LifeStories.Remember(
            target,
            MemoryKind.Betrayal,
            year,
            EventKind.ConspiracyExposed,
            leader.Id,
            plot.PlaceId,
            0.88);

        if (betrayed && world.Figures.Contains(informer))
        {
            Figure teller = world.Figures[informer];
            LifeStories.Reconcile(
                teller, target, year, EventKind.ConspiracyExposed, plot.PlaceId, 0.40, warmly: false);
            LifeStories.Embitter(
                leader, teller, year, EventKind.ConspiracyExposed, target.Id, plot.PlaceId, 0.70, fear: 0.10);
        }

        world.Chronicle.Record(
            year,
            EventKind.ConspiracyExposed,
            leader.Id,
            obj: target.Id,
            location: plot.PlaceId,
            extra: MemberIds(plot),
            data: Chronicle.Data(
                ("manner", betrayed ? "given up by one of its own" : "found by the court"),
                ("objective", ObjectiveDetail(plot.Objective)),
                ("years", (year - plot.StartYear).ToString(CultureInfo.InvariantCulture))),
            significance: Significance.Notable);

        Disputes.Consider(
            world,
            target,
            leader,
            DisputeCause.Accusation,
            EventKind.ConspiracyExposed,
            target.Id,
            year);
    }

    /// <summary>The year the plot moves, and the only year in which it can succeed.</summary>
    private static void Attempt(
        WorldState world, FigurePlot plot, Figure leader, Figure target, int year)
    {
        double chance = DetMath.Clamp(
            0.35
            + (plot.Access * 0.45)
            + (plot.WittingCount * 0.06)
            - (plot.Suspicion * 0.25)
            - (plot.Objective == PlotObjective.Depose && plot.WittingCount < 2 ? 0.15 : 0.0),
            0.08,
            0.85);

        if (!Fork(world, leader, target, year, "attempt").Chance(chance))
        {
            Close(
                world,
                plot,
                year,
                PlotOutcome.Failed,
                EventKind.ConspiracyAttempted,
                "the attempt was made and missed",
                reveal: true);
            Accuse(world, plot, leader, target, year);

            LifeStories.Remember(
                target,
                MemoryKind.Betrayal,
                year,
                EventKind.ConspiracyAttempted,
                leader.Id,
                plot.PlaceId,
                0.90);
            LifeStories.AddRivalry(
                leader, target, year, EventKind.ConspiracyAttempted, plot.PlaceId, 0.66);

            world.Chronicle.Record(
                year,
                EventKind.ConspiracyAttempted,
                leader.Id,
                obj: target.Id,
                location: plot.PlaceId,
                extra: MemberIds(plot),
                data: Chronicle.Data(("objective", ObjectiveDetail(plot.Objective))),
                significance: Significance.Notable);

            Disputes.Consider(
                world,
                target,
                leader,
                DisputeCause.Accusation,
                EventKind.ConspiracyAttempted,
                target.Id,
                year);
            return;
        }

        Close(
            world,
            plot,
            year,
            PlotOutcome.Succeeded,
            plot.Objective == PlotObjective.Depose
                ? EventKind.RulerDeposed
                : EventKind.FigureDied,
            plot.Objective == PlotObjective.Depose
                ? "the ruler was unseated by it"
                : "the ruler was killed by it",
            reveal: true);

        if (plot.Objective == PlotObjective.Depose)
        {
            Depose(world, plot, leader, target, year);
            return;
        }

        Murder(world, plot, leader, target, year);
    }

    /// <summary>
    /// The murder itself, through the ordinary death path.
    /// </summary>
    /// <remarks>
    /// Everything downstream of a political killing already existed and is reused rather than
    /// reproduced: the suspect the court names, the blood debt the family carries, the realm's
    /// grievance, and the quarrels the bereavement opens.
    /// </remarks>
    private static void Murder(
        WorldState world, FigurePlot plot, Figure leader, Figure target, int year)
    {
        IRng fate = Fork(world, leader, target, year, "manner");
        bool poison = fate.Chance(0.48);
        DeathCause cause = poison ? DeathCause.Poisoning : DeathCause.Assassination;
        string detail = poison ? "poison in the cup" : "a blade at court";

        List<Figure> family = Succession.ImmediateFamily(world, target);
        var extra = new List<EntityId>();
        foreach (Figure kin in family)
        {
            kin.KinMurderedYear = year;
            extra.Add(kin.Id);
        }

        extra.Add(leader.Id);
        foreach (PlotMember member in plot.Members)
        {
            if (member.Witting && !extra.Contains(member.FigureId)) extra.Add(member.FigureId);
        }

        var data = new DetMap<string, string>();
        world.NamePerson(data, "suspect", leader.Id);
        Accuse(world, plot, leader, target, year);

        Houses.Die(world, target, year, cause, detail, extra, data);

        if (world.Civilizations.Contains(plot.RealmId))
        {
            world.Civilizations[plot.RealmId].Fortunes.MurderAtCourt();
        }

        // Opened after the death rather than before it, so the bereavement the quarrel rests on is
        // already in the record it points at.
        foreach (Figure kin in family)
        {
            if (!kin.IsAlive || kin.Id == leader.Id) continue;

            LifeStories.Embitter(
                kin,
                leader,
                year,
                EventKind.FigureDied,
                target.Id,
                world.ResidenceOf(kin),
                grievance: 0.62,
                fear: 0.20);
            Disputes.Consider(
                world,
                kin,
                leader,
                DisputeCause.KinMurdered,
                EventKind.FigureDied,
                target.Id,
                year);
        }
    }

    /// <summary>
    /// The ruler is unseated and lives.
    /// </summary>
    /// <remarks>
    /// The throne is vacated exactly as a rising vacates it, and then either the plot's leader
    /// takes it — a claimant already had the right, and now has the court — or it is left empty for
    /// the ordinary succession to fill. The deposed ruler is disgraced rather than dead, which puts
    /// them on the same road as any other fallen office-holder: exile in all but name, and a court
    /// that may yet settle accounts.
    /// </remarks>
    private static void Depose(
        WorldState world, FigurePlot plot, Figure leader, Figure target, int year)
    {
        Civilization realm = world.Civilizations[plot.RealmId];
        Culture culture = world.CultureOf(realm);
        string title = target.OpenOffice(OfficeKind.Ruler)?.Title ?? culture.RulerTitle;

        target.EndOffice(OfficeKind.Ruler, year);
        Occupations.Sync(world, target, year);
        target.DisgracedYear = year;
        realm.CurrentRulerId = EntityId.None;
        realm.RegentId = EntityId.None;

        world.Chronicle.Record(
            year,
            EventKind.RulerDeposed,
            target.Id,
            obj: realm.Id,
            location: plot.PlaceId,
            extra: MemberIds(plot, leader.Id),
            data: Chronicle.Data(
                ("title", title),
                ("cause", "by a conspiracy of " + leader.FullName),
                ("years", (year - plot.StartYear).ToString(CultureInfo.InvariantCulture))),
            significance: Significance.Notable);

        realm.Fortunes.MurderAtCourt();
        LifeStories.Embitter(
            target, leader, year, EventKind.RulerDeposed, realm.Id, plot.PlaceId, 0.78, fear: 0.30);
        LifeStories.Remember(
            leader, MemoryKind.Triumph, year, EventKind.RulerDeposed, target.Id, plot.PlaceId, 0.80);

        // The man who unseated a ruler takes the throne only if he could have held it lawfully.
        // Where he could not, the seat is empty and the succession decides, which is the case that
        // makes a plot a political act rather than a private promotion.
        if (leader.IsAlive
            && leader.AgeIn(year) >= Succession.MajorityAge
            && !Succession.RulesElsewhere(world, leader, realm)
            && !leader.DynastyId.IsNone)
        {
            Houses.Enthrone(
                world, realm, culture, leader, year, "by the conspiracy that unseated " + target.FullName);
        }

        Disputes.Consider(
            world,
            target,
            leader,
            DisputeCause.Accusation,
            EventKind.RulerDeposed,
            realm.Id,
            year);
    }

    /// <summary>The court names the plotters, which is what makes the scaffold reachable.</summary>
    private static void Accuse(
        WorldState world, FigurePlot plot, Figure leader, Figure target, int year)
    {
        leader.AccusedYear = year;
        leader.AccusedOfId = target.Id;

        foreach (PlotMember member in plot.Members)
        {
            if (!member.Witting || !world.Figures.Contains(member.FigureId)) continue;
            if (member.FigureId == plot.BetrayerId) continue;

            Figure conspirator = world.Figures[member.FigureId];
            if (!conspirator.IsAlive) continue;

            conspirator.AccusedYear = year;
            conspirator.AccusedOfId = target.Id;
        }
    }

    /// <summary>
    /// Writes the ending into the one record every conspirator reads.
    /// </summary>
    /// <param name="reveal">
    /// Whether the world learned of it. False leaves <see cref="FigurePlot.PublicYear"/> absent, and
    /// the plot stays a fact only the export's retrospective view has.
    /// </param>
    private static void Close(
        WorldState world,
        FigurePlot plot,
        int year,
        PlotOutcome outcome,
        EventKind actKind,
        string how,
        bool reveal)
    {
        if (!plot.IsOpen) return;

        plot.Outcome = outcome;
        plot.Resolution = how;
        plot.EndYear = year;
        plot.LastActionYear = year;
        if (reveal) plot.PublicYear = year;
        plot.Acts.Add(new PlotAct(year, actKind, plot.Phase, plot.LeaderId, how, Known: reveal));
    }

    // -----------------------------------------------------------------------
    // Reading the world
    // -----------------------------------------------------------------------

    /// <summary>How much a person wants this particular ruler gone.</summary>
    internal static double Motive(Figure candidate, Figure target, bool claimant, int year)
    {
        FigureBond? bond = LifeStories.BondTo(candidate, target.Id);
        FeelingState feelings = LifeStories.Feelings(candidate, year);
        double motive = feelings.Anger * 0.32;

        if (bond is not null)
        {
            motive += bond.Grievance * 0.42;
            if (bond.Kinds.HasFlag(BondKind.Rival)) motive += 0.18;
            if (bond.Kinds.HasFlag(BondKind.Enemy)) motive += 0.20;
            motive += Math.Max(0.0, -bond.Trust) * 0.12;

            // A friend of the target wants them gone least, and the two flags are not exclusive:
            // somebody who was a friend and is now an enemy has both, and is exactly the person
            // this term should not fully excuse.
            if (bond.Kinds.HasFlag(BondKind.Friend)) motive -= 0.22;
        }

        if (claimant) motive += 0.20;
        if (candidate.DisgracedYear is int disgrace && year - disgrace <= GrudgeYears)
        {
            motive += 0.28;
        }

        return DetMath.Clamp01(motive);
    }

    /// <summary>How close this person can get to the target, by residence, office or bond.</summary>
    private static double Access(WorldState world, Figure candidate, Figure target)
    {
        double access = 0.08;
        if (world.ResidenceOf(candidate) == world.ResidenceOf(target)) access += 0.32;
        if (candidate.Occupation is Occupation.Court or Occupation.Official) access += 0.15;
        if (candidate.Offices.Exists(office => office.ToYear is null)) access += 0.15;

        FigureBond? bond = LifeStories.BondTo(candidate, target.Id);
        if (bond is not null)
        {
            BondKind privileged = BondKind.Kin
                | BondKind.Spouse
                | BondKind.Client
                | BondKind.Patron
                | BondKind.Friend;
            if ((bond.Kinds & privileged) != BondKind.None) access += 0.22;
            access += Math.Max(0.0, bond.Trust) * 0.08;
        }

        return DetMath.Clamp01(access);
    }

    /// <summary>How closely this court watches, which is mostly whether anybody is watching at all.</summary>
    private static double Vigilance(WorldState world, FigurePlot plot, Figure target)
    {
        double vigilance = 0.25 + (target.Disposition.Centralism * 0.35);

        if (world.Civilizations.Contains(plot.RealmId))
        {
            Civilization realm = world.Civilizations[plot.RealmId];
            if (Offices.HolderOf(world, realm, OfficeKind.Marshal) is not null) vigilance += 0.20;
            if (Offices.HolderOf(world, realm, OfficeKind.HighPriest) is not null) vigilance += 0.10;
        }

        return DetMath.Clamp01(vigilance);
    }

    private static bool Leads(Figure figure)
    {
        foreach (FigurePlot plot in figure.Plots)
        {
            if (plot.IsOpen) return true;
        }

        return false;
    }

    /// <summary>One plot at a time, and a breathing space after the last one.</summary>
    private static bool Available(Figure figure, int year)
    {
        foreach (FigurePlot plot in figure.Plots)
        {
            if (plot.IsOpen) return false;
            if (plot.EndYear is int ended && year - ended < CooldownYears) return false;
        }

        return true;
    }

    private static bool AnyPlotAgainst(WorldState world, Figure target)
    {
        foreach (Figure figure in world.Figures)
        {
            foreach (FigurePlot plot in figure.Plots)
            {
                if (plot.IsOpen && plot.LeaderId == figure.Id && plot.TargetId == target.Id)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int LedBy(Figure figure)
    {
        int led = 0;
        foreach (FigurePlot plot in figure.Plots)
        {
            if (plot.LeaderId == figure.Id) led++;
        }

        return led;
    }

    /// <summary>Everyone the chronicle should index a public plot event on.</summary>
    private static EntityId[]? MemberIds(FigurePlot plot, EntityId first = default)
    {
        var ids = new List<EntityId>();
        if (!first.IsNone) ids.Add(first);
        foreach (PlotMember member in plot.Members)
        {
            if (member.Witting && !ids.Contains(member.FigureId)) ids.Add(member.FigureId);
        }

        return ids.Count == 0 ? null : ids.ToArray();
    }

    /// <summary>
    /// One stream per pair, per year, per question.
    /// </summary>
    /// <remarks>
    /// Keyed on the two people rather than on the realm or on iteration position, so a plot
    /// resolves the same way whoever else was born, appointed or killed that year.
    /// </remarks>
    private static IRng Fork(
        WorldState world, Figure leader, Figure target, int year, string question) =>
        world.Root
            .Fork("plot", leader.Id.ToDiscriminator())
            .Fork("against", target.Id.ToDiscriminator())
            .Fork(question, year);

    internal static string CauseDetail(PlotCause cause) => cause switch
    {
        PlotCause.SuccessionPassedOver => "a succession they lost",
        PlotCause.OfficeRevoked => "an office taken from them",
        PlotCause.KinMurdered => "the murder of their kin",
        _ => "a quarrel they were not allowed to answer",
    };

    internal static string ObjectiveDetail(PlotObjective objective) =>
        objective == PlotObjective.Depose ? "unseating the ruler" : "the ruler's death";

    private static string TieDetail(PlotTie tie) => tie switch
    {
        PlotTie.ObligationToLeader => "for what they owed its leader",
        PlotTie.TrustInLeader => "out of trust in its leader",
        PlotTie.GrievanceAgainstTarget => "for their own grievance against the ruler",
        PlotTie.Ambition => "with a claim of their own",
        _ => "through their household",
    };
}
