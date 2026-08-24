using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>Multi-year political plots built from personal motives, relationships and access.</summary>
/// <remarks>
/// The old incident model rolled directly against a target. Here a person must first want the
/// target removed, have some route into their household, gather support, and survive the years in
/// which secrecy can fail. The final death is still an ordinary causal death; the undertaking is
/// the missing history that explains how the court reached it.
/// </remarks>
public static class Conspiracies
{
    private const int GrudgeYears = 8;

    public static void Tick(WorldState world, int year, IRng rng)
    {
        RetireStale(world, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            if (!world.Figures.Contains(civilization.CurrentRulerId)) continue;

            Figure target = world.Figures[civilization.CurrentRulerId];
            if (!target.IsAlive || target.AgeIn(year) < Succession.MajorityAge) continue;

            Figure? leader = ActiveLeader(world, civilization, target);
            IRng court = rng.Fork("court", civilization.Id.ToDiscriminator());

            if (leader is null)
            {
                leader = MaybeBegin(world, civilization, target, year, court);
                if (leader is null) continue;
            }

            FigureUndertaking? plot = Undertakings.CurrentConspiracy(leader);
            if (plot is null || plot.TargetId != target.Id) continue;

            Progress(world, civilization, leader, target, plot, year, court);
        }
    }

    private static void RetireStale(WorldState world, int year)
    {
        foreach (Figure leader in world.Figures)
        {
            FigureUndertaking? plot = Undertakings.CurrentConspiracy(leader);
            if (plot is null) continue;

            if (!leader.IsAlive)
            {
                Undertakings.Fail(world, leader, plot, year, "its leader was dead");
                continue;
            }

            if (!world.Figures.Contains(plot.TargetId) || !world.Figures[plot.TargetId].IsAlive)
            {
                Undertakings.Fail(world, leader, plot, year, "its target was already dead");
                continue;
            }

            Figure target = world.Figures[plot.TargetId];
            if (!world.Civilizations.Contains(target.CivilizationId)
                || world.Civilizations[target.CivilizationId].CurrentRulerId != target.Id)
            {
                Undertakings.Fail(world, leader, plot, year, "its target had left the throne");
            }
        }
    }

    private static Figure? ActiveLeader(
        WorldState world, Civilization civilization, Figure target)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != civilization.Id) continue;
            FigureUndertaking? plot = Undertakings.CurrentConspiracy(figure);
            if (plot is not null && plot.TargetId == target.Id) return figure;
        }

        return null;
    }

    private static Figure? MaybeBegin(
        WorldState world,
        Civilization civilization,
        Figure target,
        int year,
        IRng court)
    {
        List<Figure> claimants = Succession.Claimants(
            world, civilization, world.CultureOf(civilization), EntityId.None);
        var claimantIds = new HashSet<EntityId>();
        foreach (Figure claimant in claimants) claimantIds.Add(claimant.Id);

        Figure? best = null;
        double bestScore = 0.0;
        double bestAccess = 0.0;

        foreach (Figure candidate in world.Figures)
        {
            if (!candidate.IsAlive || candidate.Id == target.Id) continue;
            if (candidate.CivilizationId != civilization.Id) continue;
            if (candidate.AgeIn(year) < Succession.MajorityAge) continue;
            if (Undertakings.CurrentConspiracy(candidate) is not null) continue;

            double motive = Motive(candidate, target, claimantIds.Contains(candidate.Id), year);
            double access = Access(world, candidate, target);
            double score = (motive * 0.72) + (access * 0.28);

            if (score > bestScore
                || (score == bestScore && best is not null && candidate.Id.CompareTo(best.Id) < 0))
            {
                best = candidate;
                bestScore = score;
                bestAccess = access;
            }
        }

        if (best is null || bestScore < 0.26) return null;

        double chance = 0.004 + (0.020 * bestScore);
        if (!court.Fork("begin", best.Id.ToDiscriminator()).Chance(chance)) return null;

        Undertakings.BeginConspiracy(world, best, target, year, bestAccess);
        LifeStories.Remember(
            best,
            MemoryKind.Conspiracy,
            year,
            EventKind.UndertakingStarted,
            target.Id,
            world.ResidenceOf(target),
            0.78);
        return best;
    }

    private static void Progress(
        WorldState world,
        Civilization civilization,
        Figure leader,
        Figure target,
        FigureUndertaking plot,
        int year,
        IRng court)
    {
        if (!target.IsAlive)
        {
            Undertakings.Fail(world, leader, plot, year, "its target was already dead");
            return;
        }

        IRng fate = court.Fork("plot", leader.Id.ToDiscriminator());
        Recruit(world, civilization, leader, target, plot, year, fate);

        double discovery = 0.035 + ((1.0 - plot.Secrecy) * 0.24);
        if (fate.Fork("discovery", year).Chance(discovery))
        {
            Expose(world, leader, target, plot, year);
            return;
        }

        double momentum = 0.22
            + (plot.Access * 0.38)
            + (Math.Min(3, plot.ParticipantIds.Count) * 0.07);
        if (fate.Fork("advance", year).Chance(DetMath.Clamp01(momentum)))
        {
            plot.Progress++;
            plot.Secrecy = DetMath.Clamp01(plot.Secrecy - 0.06);
            plot.Steps.Add(
                new UndertakingStep(
                    year,
                    EventKind.UndertakingStarted,
                    world.ResidenceOf(target),
                    target.Id,
                    "Advanced in secret"));
        }

        if (plot.Progress < plot.RequiredProgress) return;

        double success = 0.30
            + (plot.Access * 0.42)
            + (Math.Min(3, plot.ParticipantIds.Count) * 0.06);
        if (!fate.Fork("attempt", year).Chance(DetMath.Clamp01(success)))
        {
            Expose(world, leader, target, plot, year);
            return;
        }

        Succeed(world, civilization, leader, target, plot, year, fate);
    }

    private static void Recruit(
        WorldState world,
        Civilization civilization,
        Figure leader,
        Figure target,
        FigureUndertaking plot,
        int year,
        IRng fate)
    {
        if (plot.ParticipantIds.Count >= 3) return;
        if (!fate.Fork("recruit", year).Chance(0.42)) return;

        Figure? best = null;
        double bestScore = 0.22;
        foreach (Figure candidate in world.Figures)
        {
            if (!candidate.IsAlive || candidate.Id == leader.Id || candidate.Id == target.Id) continue;
            if (candidate.CivilizationId != civilization.Id) continue;
            if (candidate.AgeIn(year) < Succession.MajorityAge) continue;
            if (plot.ParticipantIds.Contains(candidate.Id)) continue;

            FigureBond? toLeader = LifeStories.BondTo(candidate, leader.Id);
            FigureBond? toTarget = LifeStories.BondTo(candidate, target.Id);
            double trust = toLeader is null ? 0.0 : Math.Max(0.0, toLeader.Trust);
            double grievance = toTarget?.Grievance ?? 0.0;
            double score = (trust * 0.45)
                + (grievance * 0.35)
                + (Access(world, candidate, target) * 0.20);

            if (score > bestScore
                || (score == bestScore && best is not null && candidate.Id.CompareTo(best.Id) < 0))
            {
                best = candidate;
                bestScore = score;
            }
        }

        if (best is null) return;

        plot.ParticipantIds.Add(best.Id);
        plot.ParticipantIds.Sort();
        plot.Access = Math.Max(plot.Access, Access(world, best, target));
        plot.Secrecy = DetMath.Clamp01(plot.Secrecy - 0.08);
        plot.Steps.Add(
            new UndertakingStep(
                year,
                EventKind.ConspiratorJoined,
                world.ResidenceOf(target),
                best.Id,
                "Recruited"));

        LifeStories.AddConspirators(leader, best, year);
        world.Chronicle.Record(
            year,
            EventKind.ConspiratorJoined,
            leader.Id,
            obj: best.Id,
            location: world.ResidenceOf(target),
            extra: new[] { target.Id });
    }

    private static void Expose(
        WorldState world,
        Figure leader,
        Figure target,
        FigureUndertaking plot,
        int year)
    {
        plot.Steps.Add(
            new UndertakingStep(
                year,
                EventKind.ConspiracyExposed,
                world.ResidenceOf(target),
                target.Id,
                "Exposed"));
        Undertakings.Fail(world, leader, plot, year, "the court exposed it");

        leader.AccusedYear = year;
        leader.AccusedOfId = target.Id;
        LifeStories.AddRivalry(
            leader, target, year, EventKind.ConspiracyExposed, world.ResidenceOf(target), 0.72);
        LifeStories.Remember(
            target,
            MemoryKind.Betrayal,
            year,
            EventKind.ConspiracyExposed,
            leader.Id,
            world.ResidenceOf(target),
            0.88);

        world.Chronicle.Record(
            year,
            EventKind.ConspiracyExposed,
            leader.Id,
            obj: target.Id,
            location: world.ResidenceOf(target),
            extra: plot.ParticipantIds.Count == 0 ? null : plot.ParticipantIds.ToArray());
    }

    private static void Succeed(
        WorldState world,
        Civilization civilization,
        Figure leader,
        Figure target,
        FigureUndertaking plot,
        int year,
        IRng fate)
    {
        plot.Steps.Add(
            new UndertakingStep(
                year,
                EventKind.UndertakingCompleted,
                world.ResidenceOf(target),
                target.Id,
                "Succeeded"));
        Undertakings.Complete(world, leader, plot, year);

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
        foreach (EntityId participant in plot.ParticipantIds)
        {
            if (!extra.Contains(participant)) extra.Add(participant);
        }

        var data = new DetMap<string, string>();
        world.NamePerson(data, "suspect", leader.Id);
        leader.AccusedYear = year;
        leader.AccusedOfId = target.Id;

        Houses.Die(world, target, year, cause, detail, extra, data);
        civilization.Fortunes.MurderAtCourt();
    }

    private static double Motive(Figure candidate, Figure target, bool claimant, int year)
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
        }

        if (claimant) motive += 0.20;
        if (candidate.DisgracedYear is int disgrace && year - disgrace <= GrudgeYears)
        {
            motive += 0.28;
        }

        return DetMath.Clamp01(motive);
    }

    private static double Access(WorldState world, Figure candidate, Figure target)
    {
        double access = 0.08;
        if (world.ResidenceOf(candidate) == world.ResidenceOf(target)) access += 0.32;
        if (candidate.Occupation is Occupation.Court or Occupation.Official) access += 0.15;
        if (candidate.Offices.Exists(office => office.ToYear is null)) access += 0.15;

        FigureBond? bond = LifeStories.BondTo(candidate, target.Id);
        if (bond is not null)
        {
            BondKind privileged = BondKind.Kin | BondKind.Spouse | BondKind.Client | BondKind.Patron;
            if ((bond.Kinds & privileged) != BondKind.None) access += 0.22;
            access += Math.Max(0.0, bond.Trust) * 0.08;
        }

        return DetMath.Clamp01(access);
    }
}
