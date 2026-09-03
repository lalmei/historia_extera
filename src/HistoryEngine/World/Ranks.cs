using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// The ladder inside a realm's army, and what it takes to climb it.
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> Soldiery was the commonest career in the world and the emptiest.
/// A figure took to arms at sixteen, was drawn to a field now and then by
/// <see cref="Campaigns.NoteSoldiers"/>, and was exactly what they had always been when they died
/// at sixty — unless a court happened to make them marshal, which one soldier in a generation ever
/// was. A career with one rung is not a career, and the chronicle had no way to say that a man had
/// risen.</para>
///
/// <para><b>Every rung changes what some other system does</b>, which is the standard the office
/// model set and the reason this is not decoration. <see cref="Warfare"/> hands the command to a
/// realm's ranking officer when the crown has no marshal to send, and weighs a battle by who is
/// standing at the front of it; <see cref="Campaigns"/> puts officers on more fields than
/// recruits; <c>OfficeSystem</c> reaches for a captain when it wants a marshal. A soldier's rank
/// is therefore a thing his realm's history turns on, not a label on his page.</para>
///
/// <para><b>The mutating counterpart to nothing.</b> Unlike <see cref="Offices"/> there is no
/// reading half to keep apart from a writing half, because a rank has no politics: nobody grants
/// it, nobody can be stripped of it, and no other realm has an opinion about it. What it has
/// instead is an establishment — see <see cref="Establishment"/> — which is what stops a small
/// army from being all captains.</para>
///
/// <para>Kept out of the yearly system for the reason <see cref="Offices"/> is: two paths reach a
/// rank. A soldier climbs to it, and a marshal arrives at the top of it the day he is appointed,
/// and both must assemble the step identically.</para>
/// </remarks>
public static class Ranks
{
    /// <summary>The rung a marshal stands on, and the top of every realm's ladder.</summary>
    public const MilitaryRank Top = MilitaryRank.Commander;

    /// <summary>Yearly chance a soldier who is due and has a place to go is actually raised.</summary>
    /// <remarks>
    /// Not one, for the reason no seat is filled the year it falls vacant: an army that promoted
    /// every eligible soldier the season he became eligible would turn its whole establishment over
    /// in a decade, and the ladder would read as a schedule rather than as a set of decisions.
    /// </remarks>
    private const double BasePromotion = 0.25;

    /// <summary>How much each point of battle renown adds to that chance.</summary>
    /// <remarks>
    /// The substance of the model, and the reason the whole thing is worth having: the way up is
    /// the field. A soldier who has been noticed once at a victory climbs roughly a third faster
    /// than one who has not, and a commander who has been noticed three times climbs twice as
    /// fast — which is what makes a war produce officers rather than only casualties.
    /// </remarks>
    private const double RenownWeight = 0.34;

    /// <summary>The claim recorded on the rung a marshalcy puts its holder on.</summary>
    /// <remarks>
    /// A constant for the reason <see cref="Offices.CustomaryClaim"/> is one: it is the only trace
    /// in the export that distinguishes a rung somebody was appointed onto from one they climbed,
    /// and the establishment is deliberately blind to the difference — see
    /// <see cref="Establishment"/>.
    /// </remarks>
    public const string CommissionClaim = "on taking the realm's command";

    /// <summary>
    /// Renown a soldier must have earned at a battle before this rung is open to him at all.
    /// </summary>
    /// <remarks>
    /// <para><b>The hard gate, and the thing that makes the ladder about the field.</b> Waiting is
    /// a qualification for the rungs below: most of what a file leader does is turn up for twenty
    /// years, and a realm at peace still needs somebody to hold the men together. It is not a
    /// qualification for the ones above. Nobody is handed a wing for seniority, and a realm that
    /// has never noticed a man at a battle has no reason to believe he can win one.</para>
    ///
    /// <para><b>Measured, because the first cut of this model had no such gate and the ladder
    /// turned out to be a function of survival.</b> Soldiers who had been noticed at a battle
    /// finished their careers at an average rank of 4.06 and soldiers who never had at 4.08 —
    /// everyone who lived past forty made captain, because places kept falling vacant and merit
    /// only ever decided which of two men took one in the same year, which in a muster of four
    /// almost never happens. Renown weighting the odds cannot fix that on its own; what the field
    /// has to buy is the rung itself.</para>
    /// </remarks>
    public static int NeedsRenown(MilitaryRank rank) => rank switch
    {
        MilitaryRank.Commander => 2,
        MilitaryRank.Captain => 1,
        _ => 0,
    };

    /// <summary>Whether this figure is somebody an army would have a rank for.</summary>
    /// <remarks>
    /// Arms, or the realm's command. A marshal drawn from a merchant's house is of the army for as
    /// long as he holds the seat — <see cref="Occupations.Sync"/> has already put him in arms — and
    /// the check names the office as well so that the one path into the top rung does not depend on
    /// the order two systems happen to run in.
    /// </remarks>
    public static bool Serves(Figure figure, int year) =>
        figure.IsAlive
        && figure.AgeIn(year) >= Succession.MajorityAge
        && (figure.Occupation == Occupation.Soldiery || figure.Holds(OfficeKind.Marshal));

    /// <summary>The rung above this one, or the same rung at the top of the ladder.</summary>
    public static MilitaryRank Next(MilitaryRank rank) => rank switch
    {
        MilitaryRank.None => MilitaryRank.Recruit,
        MilitaryRank.Recruit => MilitaryRank.Soldier,
        MilitaryRank.Soldier => MilitaryRank.FileLeader,
        MilitaryRank.FileLeader => MilitaryRank.Captain,
        _ => MilitaryRank.Commander,
    };

    /// <summary>
    /// Years a soldier holds a rung before the next one is considered.
    /// </summary>
    /// <remarks>
    /// Career length again, the argument <c>Offices.CareerAge</c> makes about appointment ages
    /// applied to the rungs below them. The intervals lengthen going up, so that the full climb
    /// takes about twenty-two years at the earliest — a man who took to arms at sixteen can be his
    /// realm's commander in his forties, and only if the field has noticed him.
    /// </remarks>
    public static int Seasoning(MilitaryRank rank) => rank switch
    {
        MilitaryRank.Recruit => 3,
        MilitaryRank.Soldier => 5,
        MilitaryRank.FileLeader => 6,
        MilitaryRank.Captain => 8,
        _ => 0,
    };

    /// <summary>
    /// How many soldiers a realm keeps at this rung or above, out of a muster of
    /// <paramref name="soldiers"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>An army is a shape, not a queue.</b> Without this every soldier who lived long
    /// enough would end a commander, and a realm's chronicle would be a list of men being promoted
    /// past each other into the same job. The establishment is what makes a promotion require
    /// somebody else's death or elevation, which is both how it worked and what makes a rank worth
    /// recording.</para>
    ///
    /// <para>Counted at or above, so a commander occupies a captain's place as well as his own —
    /// the alternative is a six-man muster with a commander, a captain and two serjeants in it. The
    /// <see cref="MilitaryRank.Commander"/> place is one per realm whatever the muster, and a
    /// sitting marshal is standing in it: a realm with a marshal has no room at the top until the
    /// seat empties, which is the whole of the relationship between the ladder and the office.</para>
    ///
    /// <para>The muster is the soldiers the chronicle follows, not the levy — a median of four
    /// living soldiers a realm, measured, against a levy of thousands. The divisors are set against
    /// that measurement and describe the shape of the recorded officer corps rather than of an
    /// army: a realm of four keeps a captain and a file leader under him, and one of fifteen keeps
    /// three and seven.</para>
    ///
    /// <para><b>It governs promotion, not existence.</b> A realm can carry more than this allows,
    /// by two routes that are arrivals rather than promotions: a marshal is put on the top rung by
    /// his appointment, and a captain who took a governorship and came back to arms brings his rung
    /// back with him. Refusing either costs more than the excess — the first gives a realm a
    /// marshal outranked by his own captains, and the second resets a career every time somebody
    /// serves a term in an office. What the pass guarantees is that nobody is <em>raised</em> into
    /// a place the establishment does not have.</para>
    /// </remarks>
    public static int Establishment(MilitaryRank rank, int soldiers) => rank switch
    {
        MilitaryRank.Commander => 1,
        MilitaryRank.Captain => Math.Max(1, soldiers / 4),
        MilitaryRank.FileLeader => Math.Max(1, soldiers / 2),
        _ => int.MaxValue,
    };

    /// <summary>Everyone of this realm an army would have a rank for, in id order.</summary>
    public static List<Figure> Muster(WorldState world, Civilization civilization, int year)
    {
        var soldiers = new List<Figure>();
        foreach (Figure figure in world.Figures)
        {
            if (figure.CivilizationId != civilization.Id) continue;
            if (!Serves(figure, year)) continue;

            soldiers.Add(figure);
        }

        return soldiers;
    }

    /// <summary>
    /// How many of a muster stand at this rung or above, for the purpose of the establishment.
    /// </summary>
    /// <remarks>
    /// <b>The marshal's seat is above the ladder, not the top of it.</b> He stands in the realm's
    /// one commander's place and in none of the places below it — counted into those as well, a
    /// muster of four had its single captaincy filled by a man who was not a captain, and the
    /// ladder starved: measured before this rule, five worlds produced 566 commanders and two
    /// captains, which is not a pyramid but a court appointment with an empty army under it. A
    /// marshal who lays the seat down is an officer again and occupies a place like anyone else.
    /// </remarks>
    public static int Standing(IReadOnlyList<Figure> muster, MilitaryRank rank)
    {
        int held = 0;
        foreach (Figure soldier in muster)
        {
            if (soldier.Rank < rank) continue;
            if (rank < Top && soldier.Holds(OfficeKind.Marshal)) continue;

            held++;
        }

        return held;
    }

    /// <summary>
    /// Puts somebody on a rung and records it.
    /// </summary>
    /// <remarks>
    /// The single assembly point both paths go through, for the reason <see cref="Offices.Grant"/>
    /// is one: a marshal commissioned at the founding of a realm and a serjeant raised in its third
    /// century must produce the same row, or one of them is a step the viewer cannot render.
    /// </remarks>
    public static void Raise(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Figure soldier,
        MilitaryRank rank,
        string? claim,
        int year)
    {
        if (rank == MilitaryRank.None || soldier.Rank >= rank) return;

        string title = culture.RankTitle(rank);
        soldier.Service.Add(
            new RankStep(rank, title, civilization.Id, year) { Claim = claim });

        DetMap<string, string> data = claim is null
            ? Chronicle.Data(("rank", title))
            : Chronicle.Data(("rank", title), ("claim", claim));

        // The lower rungs are the parish register of an army: everyone who ever took to arms
        // reaches them, and a spine carrying all of them would bury the year's war under its own
        // recruitment. A captaincy is a decision a realm made about a person, and stays.
        world.Chronicle.Record(
            year,
            EventKind.RankGranted,
            soldier.Id,
            obj: civilization.Id,
            location: world.ResidenceOf(soldier),
            data: data,
            significance: rank >= MilitaryRank.Captain
                ? Significance.Notable
                : Significance.Routine);
    }

    /// <summary>Swears in somebody of arms who has no rank yet.</summary>
    public static void Enlist(
        WorldState world, Civilization civilization, Culture culture, Figure soldier, int year)
    {
        if (soldier.Rank != MilitaryRank.None) return;

        Raise(world, civilization, culture, soldier, MilitaryRank.Recruit, null, year);
    }

    /// <summary>
    /// Puts a newly seated marshal at the top of his realm's ladder.
    /// </summary>
    /// <remarks>
    /// <para>Not a promotion the army decided on — the appointment is the decision, and this is
    /// what the appointment means. A realm's marshal is its ranking soldier by definition, and
    /// leaving him wherever the ladder had got to would give a realm a commander and a
    /// war-leader who were two different people with the second outranked by the first.</para>
    ///
    /// <para>Silent. The grant has already been written, and a second line saying the same court
    /// did the same thing on the same day is the duplication <see cref="EventKind.OfficeGranted"/>
    /// already refuses for consorts.</para>
    /// </remarks>
    public static void Commission(
        WorldState world, Civilization civilization, Culture culture, Figure marshal, int year)
    {
        if (marshal.Rank >= Top) return;

        string title = culture.RankTitle(Top);
        marshal.Service.Add(
            new RankStep(Top, title, civilization.Id, year) { Claim = CommissionClaim });
    }

    /// <summary>
    /// The one soldier of this realm a court would hand a campaign to.
    /// </summary>
    /// <remarks>
    /// Highest rung first, then longest in it, then id — a total order, so the answer does not
    /// depend on the table's iteration having happened to reach them first. Nobody below
    /// <see cref="MilitaryRank.FileLeader"/> is offered a host, and neither is anyone the years or
    /// a bad campaign have left unfit: <see cref="Warfare"/> asks this exactly where it used to
    /// find a cousin, and a broken serjeant is not an improvement on one.
    /// </remarks>
    public static Figure? RankingOfficer(WorldState world, Civilization civilization, int year)
    {
        Figure? best = null;
        RankStep? bestStep = null;

        foreach (Figure soldier in Muster(world, civilization, year))
        {
            if (soldier.Rank < MilitaryRank.FileLeader) continue;
            if (LifeStories.Fitness(soldier, year) <= 0.0) continue;
            if (Campaigns.Readiness(soldier, year) <= 0.0) continue;

            RankStep step = soldier.CurrentRank!;
            if (best is null
                || step.Rank > bestStep!.Rank
                || (step.Rank == bestStep.Rank && step.Year < bestStep.Year))
            {
                best = soldier;
                bestStep = step;
            }
        }

        return best;
    }

    /// <summary>
    /// How much likelier than a man of the line this rank is to be at a given field.
    /// </summary>
    /// <remarks>
    /// An officer goes where the host goes; a recruit is whoever was left at home this season.
    /// Read by <see cref="Campaigns.NoteSoldiers"/>, which is what turns a rank into more battles
    /// and more battles into the renown that is the way up — the loop the whole model runs on.
    /// </remarks>
    public static double Turnout(MilitaryRank rank) => rank switch
    {
        MilitaryRank.Recruit => 0.85,
        MilitaryRank.FileLeader => 1.15,
        MilitaryRank.Captain => 1.30,
        MilitaryRank.Commander => 1.50,
        _ => 1.0,
    };

    /// <summary>What standing at the front of this army is worth, as a multiple of its strength.</summary>
    /// <remarks>
    /// <para>The engine already paid a flat eight per cent for having a named commander at all,
    /// which said only that somebody was there. Who it is now matters: a realm's own commander is
    /// worth about twice a cousin the court could spare, and that is the payoff for the ladder
    /// existing — a realm that has kept its officers alive fights better than one that has not.</para>
    ///
    /// <para>Modest at the top on purpose. A rank should tilt a battle, never decide it; the
    /// largest term on this field is still the wall.</para>
    /// </remarks>
    public static double CommandBonus(MilitaryRank rank) => rank switch
    {
        MilitaryRank.FileLeader => 1.10,
        MilitaryRank.Captain => 1.13,
        MilitaryRank.Commander => 1.16,
        _ => 1.08,
    };

    /// <summary>What a rung is worth to a court weighing candidates for its marshalcy.</summary>
    /// <remarks>
    /// Denominated in battle renown, which is the other half of that score: a captain is worth
    /// about one commanded victory, so a realm's own senior officer beats a courtier who has been
    /// noticed once and loses to one who has been noticed three times. That is the intended shape.
    /// A court prefers its army's own, and a genuinely famous outsider still takes the seat.
    /// </remarks>
    public static double Weight(MilitaryRank rank) => 0.75 * (int)rank;

    /// <summary>What a soldier is worth to an army choosing who to raise into one free place.</summary>
    /// <remarks>
    /// <para>Renown, and a per-candidate tie-break drawn on the soldier's own id so that adding a
    /// soldier to a muster cannot reshuffle the order of everyone already in it — the same
    /// arrangement <c>OfficeSystem.PickCandidate</c> uses to choose a marshal, and for the same
    /// reason.</para>
    ///
    /// <para><b>Merit rather than seniority, measured.</b> The first cut of this model offered
    /// each free place to whoever came first in id order, which for figures is roughly the order
    /// they were born. It produced a ladder that renown had no effect on at all: soldiers who had
    /// been noticed at a battle ended their careers at rank 3.49 on average and soldiers who never
    /// were at 3.50, because the places were scarce and the queue, not the field, decided them.
    /// </para>
    /// </remarks>
    public static double Merit(Figure soldier, IRng rng) =>
        Campaigns.Renown(soldier)
        + rng.Fork("merit", soldier.Id.ToDiscriminator()).NextDouble();

    /// <summary>
    /// Whether this soldier is due for the next rung, and how likely he is to reach it this year.
    /// </summary>
    /// <remarks>
    /// Zero where he is not due at all — too new to the rung, unfit, or without the renown the top
    /// of the ladder asks for — so a caller can treat the whole judgement as one number and the
    /// yearly pass stays a loop over soldiers rather than a nest of conditions.
    /// </remarks>
    public static double PromotionOdds(Figure soldier, int year)
    {
        RankStep? step = soldier.CurrentRank;
        if (step is null) return 0.0;

        MilitaryRank next = Next(step.Rank);
        if (next == step.Rank) return 0.0;
        if (year - step.Year < Seasoning(step.Rank)) return 0.0;

        int renown = Campaigns.Renown(soldier);
        if (renown < NeedsRenown(next)) return 0.0;

        double fitness = LifeStories.Fitness(soldier, year);
        if (fitness <= 0.0) return 0.0;

        // The higher the rung the fewer the years it is offered in, on top of the establishment
        // already refusing most of them a place. Together they are what keeps a full climb rare.
        double reach = next switch
        {
            MilitaryRank.Commander => 0.60,
            MilitaryRank.Captain => 0.75,
            MilitaryRank.FileLeader => 0.90,
            _ => 1.0,
        };

        return DetMath.Clamp01(
            BasePromotion * (1.0 + (RenownWeight * renown)) * reach * fitness);
    }

    /// <summary>
    /// The field this promotion is owed to, in prose, or null where it is owed to service alone.
    /// </summary>
    /// <remarks>
    /// <para>Only battles fought since the last rung, so a captaincy cannot be credited to the
    /// victory that already made the man a serjeant. Reading the whole career instead gave every
    /// promotion of a long life the same citation, which reads as a chronicler who knows one fact
    /// about somebody.</para>
    ///
    /// <para>Deliberately does not consume the renown the way <see cref="Campaigns.PromotionCause"/>
    /// does for a marshalcy. A battle can carry a man up a rung and later into the seat, and the
    /// marshal's citation is the one that would be lost if this spent it.</para>
    /// </remarks>
    public static string? Citation(WorldState world, Figure soldier, int since)
    {
        CampaignMemory? best = null;
        foreach (CampaignMemory memory in soldier.Campaigns)
        {
            if (memory.RenownGained <= 0 || memory.Year < since) continue;
            if (!world.Battles.Contains(memory.BattleId)) continue;
            if (best is null
                || memory.RenownGained > best.RenownGained
                || (memory.RenownGained == best.RenownGained && memory.Year > best.Year))
            {
                best = memory;
            }
        }

        return best is null ? null : "for service at " + world.NameOf(best.BattleId);
    }
}
