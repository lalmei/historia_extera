using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Marries the houses off and fills their nurseries.
/// </summary>
/// <remarks>
/// <para><b>The problem this system exists to solve is not fertility, it is attention.</b> A house
/// where everyone marries and every couple has children grows exponentially: three children a
/// generation over ten generations is fifty thousand people per realm, and a history generator that
/// runs in milliseconds stops doing so. Capping births would bound the count and produce a
/// chronicle of implausibly small families.</para>
///
/// <para>What is capped instead is <em>proximity to the throne</em>. A figure is married off, and a
/// couple has children, only while they stand near the front of their house's line — see
/// <see cref="Succession.Kin"/>. As the ruler's own children are born, cousins are pushed down the
/// line and quietly stop being written about. That is not a claim about who had children; it is a
/// claim about whose children a chronicle bothers to name, which is exactly the right thing for
/// this engine to be modelling, and it makes the figure table grow linearly in the number of
/// reigns rather than exponentially in the number of generations.</para>
///
/// <para>Runs after <see cref="SuccessionSystem"/>, so the year's marriages and births are measured
/// against the line as it stands after any coronation — a new king's brothers are demoted the same
/// year he is crowned, not the year after.</para>
/// </remarks>
public sealed class HouseholdSystem : ISystem
{
    /// <summary>Youngest age at which a figure may be married.</summary>
    private const int MarriageAge = 16;

    /// <summary>Rank in the line beyond which the chronicle stops arranging marriages.</summary>
    private const int MarriageableRank = 8;

    /// <summary>
    /// Rank beyond which a couple's children go unrecorded.
    /// </summary>
    /// <remarks>
    /// Six positions rather than the four an eye for tidiness suggests, and the difference is not
    /// cosmetic. Most of a line at any moment is children and the elderly, so four positions leave
    /// a house with barely one couple of childbearing age — and one couple against a fifth infant
    /// mortality is a house that fails within three generations. At six, a world keeps a spread of
    /// houses alive instead of converging on the two that got lucky early.
    /// </remarks>
    private const int FertileRank = 5;

    /// <summary>
    /// Ranks added to every member of a house that holds no throne.
    /// </summary>
    /// <remarks>
    /// Chosen against the two gates above so that a house out of power is followed at its head and
    /// nowhere else: one couple still has children, three more members are still married off, and
    /// the rest of the family passes out of the record. That is enough for a house to survive a
    /// century in opposition and be elected back — and little enough that a world's dormant houses
    /// do not between them outnumber its reigning ones.
    /// </remarks>
    private const int DormantHouseRank = 5;

    /// <summary>Yearly chance an eligible unmarried figure is matched.</summary>
    private const double MarriageChance = 0.34;

    /// <summary>Base chance a fertile marriage produces a child in a given year.</summary>
    private const double BirthChance = 0.26;

    /// <summary>Chance a birth kills the mother.</summary>
    private const double ChildbedRisk = 0.022;

    /// <summary>Age past which a woman bears no further recorded children.</summary>
    private const int LastChildbearingAge = 44;

    /// <summary>Base odds a match is sought among the other houses rather than at home.</summary>
    private const double ForeignMatchFloor = 0.25;

    /// <summary>Extra odds of a foreign match at full <see cref="CultureValues.Mercantile"/>.</summary>
    private const double ForeignMatchFromTrade = 0.5;

    public string Name => "houses";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        IRng rng = world.Root.Fork(Name, year);
        DetMap<EntityId, int> ranks = RankEveryHouse(world);

        Marry(world, ranks, year, rng);
        Bear(world, ranks, year, rng);
    }

    /// <summary>
    /// Every living dynast's distance from the nearest throne their house has a claim on.
    /// </summary>
    /// <remarks>
    /// <para>A <see cref="DetMap{TKey,TValue}"/> rather than a dictionary: this is built once a year
    /// and read for every figure, and a dictionary's iteration order would leak into which of two
    /// equally-ranked cousins got married first — see <c>DeterminismGuardTests</c>.</para>
    ///
    /// <para><b>The budget is per court, not per family.</b> A house is ranked once for every
    /// throne it holds, so the number of people a world follows tracks the number of realms in it
    /// rather than the number of surviving houses. Ranking per house instead is the tidier reading
    /// and quietly starves a long run: houses consolidate, and an eight-century world ends with
    /// two families and fifty living people in it. What made per-court ranking a runaway — a
    /// larger house winning more elections and so breeding faster still — is dealt with where it
    /// happens, in <see cref="Succession.Claimants"/>, which passes over a house that already
    /// rules elsewhere.</para>
    ///
    /// <para>Reigning houses are ranked first and dormant ones second, taking the lower of the two
    /// wherever they overlap. A house that rules somewhere is therefore never penalised for also
    /// being out of power somewhere else, and the second pass needs no test for which houses the
    /// first one already covered.</para>
    /// </remarks>
    private static DetMap<EntityId, int> RankEveryHouse(WorldState world)
    {
        var ranks = new DetMap<EntityId, int>();

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Rank(ranks, Succession.Kin(world, civilization), 0);
        }

        foreach (Dynasty house in world.Dynasties)
        {
            if (house.IsExtinct) continue;

            Rank(ranks, Succession.Kin(world, house), DormantHouseRank);
        }

        return ranks;
    }

    private static void Rank(DetMap<EntityId, int> ranks, List<Figure> line, int offset)
    {
        for (int i = 0; i < line.Count; i++)
        {
            int rank = i + offset;

            // A dynast standing in two lines takes the nearer throne's rank.
            if (!ranks.TryGetValue(line[i].Id, out int existing) || rank < existing)
            {
                ranks[line[i].Id] = rank;
            }
        }
    }

    private static void Marry(
        WorldState world, DetMap<EntityId, int> ranks, int year, IRng rng)
    {
        // Id order — birth order — so an older sibling is matched before a younger one, and the
        // pass does not depend on how the ranking happened to be assembled. Bounded to the figures
        // already on the books, since matching one adds their partner to the table behind us.
        int known = world.Figures.Count;

        for (int i = 0; i < known; i++)
        {
            Figure figure = world.Figures[i];
            if (!Marriageable(world, figure, ranks, year)) continue;
            if (!rng.Chance(MarriageChance)) continue;

            Culture culture = world.CultureOf(figure);
            Figure partner = FindPartner(world, figure, culture, year, rng)
                ?? MatchAtHome(world, figure, culture, year, rng);

            Wed(world, figure, partner, ranks, year);
        }
    }

    private static bool Marriageable(
        WorldState world, Figure figure, DetMap<EntityId, int> ranks, int year)
    {
        if (!figure.IsAlive || figure.IsMarried) return false;
        if (figure.AgeIn(year) < MarriageAge) return false;

        // Only dynasts are matched. A consort's own remarriage would need a second house to be
        // interested in them, and by then the chronicle is following their children instead.
        if (figure.DynastyId.IsNone) return false;
        if (!InAStandingRealm(world, figure)) return false;

        return ranks.TryGetValue(figure.Id, out int rank) && rank <= MarriageableRank;
    }

    /// <summary>
    /// Whether a figure lives somewhere a marriage could be made or a birth recorded.
    /// </summary>
    /// <remarks>
    /// A house whose realm has fallen keeps its members and its claims, but the chronicle has no
    /// seat to name and no court to hold — so it stops adding to itself until one of its own
    /// marries back into a realm that still stands.
    /// </remarks>
    private static bool InAStandingRealm(WorldState world, Figure figure) =>
        world.Civilizations.Contains(figure.CivilizationId)
        && world.Civilizations[figure.CivilizationId].IsActive;

    /// <summary>
    /// Looks for a match among the other houses.
    /// </summary>
    /// <remarks>
    /// <para>This is where a marriage becomes more than bookkeeping: it ties two houses together,
    /// moves a person between realms, and — when the line later runs through them — carries one
    /// house's claim into another's territory. <see cref="CultureValues.Mercantile"/> decides how
    /// outward-looking a people is about it, so a trading culture's family tree reaches across the
    /// map and an insular one marries its own neighbours.</para>
    ///
    /// <para>Same-house matches are refused outright rather than merely discouraged. Permitting
    /// them would put both partners in one line of succession, and a couple would then be counted
    /// twice by everything downstream that walks a house.</para>
    /// </remarks>
    private static Figure? FindPartner(
        WorldState world, Figure figure, Culture culture, int year, IRng rng)
    {
        double abroad = ForeignMatchFloor + ForeignMatchFromTrade * culture.Values.Mercantile;
        if (!rng.Chance(abroad)) return null;

        var candidates = new List<Figure>();

        foreach (Dynasty house in world.Dynasties)
        {
            if (house.Id == figure.DynastyId) continue;

            foreach (EntityId id in house.MemberIds)
            {
                Figure candidate = world.Figures[id];

                if (!candidate.IsAlive || candidate.IsMarried) continue;
                if (candidate.Sex == figure.Sex) continue;

                int age = candidate.AgeIn(year);
                if (age < MarriageAge || age > LastChildbearingAge) continue;

                if (!InAStandingRealm(world, candidate)) continue;

                // A reigning ruler is not available to marry into someone else's realm: one of the
                // pair would have to move, and a crowned head that moves is a personal union, which
                // belongs to Milestone 6's diplomacy rather than to a marriage roll. It also fixes
                // an opening-year artefact — every founding ruler is unmarried in year one, so
                // without this the eight of them pair off with each other and half the founding
                // houses are absorbed into the other half before the chronicle has begun.
                if (Succession.HoldsAThrone(world, candidate)) continue;

                if (Succession.AreCloseKin(world, figure, candidate)) continue;

                candidates.Add(candidate);
            }
        }

        return candidates.Count == 0 ? null : rng.Pick(candidates);
    }

    /// <summary>A partner of no recorded house, from the figure's own realm.</summary>
    private static Figure MatchAtHome(
        WorldState world, Figure figure, Culture culture, int year, IRng rng)
    {
        Civilization civilization = world.Civilizations[figure.CivilizationId];
        Sex sex = figure.Sex == Sex.Male ? Sex.Female : Sex.Male;

        return Houses.NewFigure(
            world, civilization, culture, sex, year - rng.NextInt(MarriageAge, 27));
    }

    /// <summary>
    /// Records a marriage, moving whichever partner has the weaker claim.
    /// </summary>
    /// <remarks>
    /// Weaker claim rather than sex, so a foreign prince marrying a reigning queen moves to her
    /// realm as readily as the reverse — and his house, being his children's, arrives with him.
    /// Id breaks the tie so the choice never depends on iteration order.
    /// </remarks>
    private static void Wed(
        WorldState world, Figure figure, Figure partner, DetMap<EntityId, int> ranks, int year)
    {
        figure.SpouseId = partner.Id;
        partner.SpouseId = figure.Id;
        figure.SpouseIds.Add(partner.Id);
        partner.SpouseIds.Add(figure.Id);

        Figure mover = WhoMoves(world, figure, partner, ranks);
        Figure stays = mover.Id == figure.Id ? partner : figure;
        mover.CivilizationId = stays.CivilizationId;

        EntityId seat = world.Civilizations.Contains(stays.CivilizationId)
            ? world.Civilizations[stays.CivilizationId].CapitalId
            : EntityId.None;

        world.Chronicle.Record(
            year,
            EventKind.FigureMarried,
            figure.Id,
            obj: partner.Id,
            location: seat,
            extra: HousesJoined(figure, partner));
    }

    /// <summary>
    /// Which partner leaves home. The weaker claim, and never a crowned head.
    /// </summary>
    /// <remarks>
    /// The throne check is not a nicety. A ruler moved out of the realm they rule leaves
    /// <see cref="Civilization.CurrentRulerId"/> pointing at someone who now lives somewhere else,
    /// and <see cref="Houses.Die"/> finds the civilization to vacate through the dead figure's own
    /// residence — so their death empties the wrong throne and leaves the right one occupied by a
    /// corpse. Succession sees a realm that has a ruler and skips it, for ever. Three realms in a
    /// three-century run were governed by the dead for over a century each before this, and
    /// nothing in the chronicle said so: the events simply stopped.
    /// </remarks>
    private static Figure WhoMoves(
        WorldState world, Figure a, Figure b, DetMap<EntityId, int> ranks)
    {
        if (Succession.HoldsAThrone(world, a)) return b;
        if (Succession.HoldsAThrone(world, b)) return a;

        int rankA = ranks.TryGetValue(a.Id, out int found) ? found : int.MaxValue;
        int rankB = ranks.TryGetValue(b.Id, out int other) ? other : int.MaxValue;

        if (rankA != rankB) return rankA > rankB ? a : b;
        return a.Id.CompareTo(b.Id) > 0 ? a : b;
    }

    /// <summary>The houses a marriage joins, so the event lands on both of their pages.</summary>
    private static EntityId[]? HousesJoined(Figure a, Figure b)
    {
        if (a.DynastyId.IsNone && b.DynastyId.IsNone) return null;
        if (b.DynastyId.IsNone || b.DynastyId == a.DynastyId) return new[] { a.DynastyId };
        if (a.DynastyId.IsNone) return new[] { b.DynastyId };

        return new[] { a.DynastyId, b.DynastyId };
    }

    /// <summary>
    /// Children, walked over mothers rather than couples.
    /// </summary>
    /// <remarks>
    /// A couple has exactly one mother and may have partners drawn from two houses, so iterating
    /// mothers is what guarantees each household is considered once a year. Iterating houses
    /// instead would give a cross-house couple two chances at a child and none of the obvious
    /// guards against it survive contact with a house that rules two realms.
    /// </remarks>
    private static void Bear(
        WorldState world, DetMap<EntityId, int> ranks, int year, IRng rng)
    {
        // Bounded like the marriage pass: a child born this year joins the table behind us, and is
        // not a candidate for anything until the next.
        int known = world.Figures.Count;

        for (int i = 0; i < known; i++)
        {
            Figure mother = world.Figures[i];
            if (mother.Sex != Sex.Female || !mother.IsAlive || !mother.IsMarried) continue;
            if (!InAStandingRealm(world, mother)) continue;

            int age = mother.AgeIn(year);
            if (age < MarriageAge || age > LastChildbearingAge) continue;

            Figure father = world.Figures[mother.SpouseId];
            if (!father.IsAlive) continue;

            if (!NearEnoughToTheThrone(ranks, mother, father)) continue;
            if (!rng.Chance(BirthChance * Fecundity(age))) continue;

            Deliver(world, mother, father, year, rng);
        }
    }

    private static bool NearEnoughToTheThrone(
        DetMap<EntityId, int> ranks, Figure mother, Figure father)
    {
        bool near = ranks.TryGetValue(mother.Id, out int hers) && hers <= FertileRank;
        return near || (ranks.TryGetValue(father.Id, out int his) && his <= FertileRank);
    }

    /// <summary>
    /// How likely a birth is at a given age, as a fraction of the base rate.
    /// </summary>
    /// <remarks>
    /// Piecewise-linear rather than a curve: the shape wanted here is a plateau through the
    /// twenties falling away through the thirties, and every smooth function that produces it
    /// needs a transcendental this engine will not use on a decision path.
    /// </remarks>
    private static double Fecundity(int age)
    {
        if (age < 20) return DetMath.Lerp(0.45, 1.0, DetMath.InverseLerp(16.0, 20.0, age));
        if (age <= 32) return 1.0;

        return DetMath.Lerp(1.0, 0.12, DetMath.InverseLerp(32.0, LastChildbearingAge, age));
    }

    private static void Deliver(
        WorldState world, Figure mother, Figure father, int year, IRng rng)
    {
        // The child takes the father's house, and the mother's only where there is no father's
        // house to take — which is what lets a queen's line continue when her consort has none.
        Figure heirOf = father.DynastyId.IsNone ? mother : father;
        Civilization civilization = world.Civilizations[mother.CivilizationId];
        Culture culture = world.CultureOf(heirOf);

        Sex sex = rng.Chance(0.5) ? Sex.Male : Sex.Female;
        Figure child = Houses.NewFigure(world, civilization, culture, sex, year);

        child.MotherId = mother.Id;
        child.FatherId = father.Id;
        child.DynastyId = heirOf.DynastyId;

        mother.ChildIds.Add(child.Id);
        father.ChildIds.Add(child.Id);

        if (world.Dynasties.Contains(child.DynastyId))
        {
            world.Dynasties[child.DynastyId].MemberIds.Add(child.Id);
        }

        world.Chronicle.Record(
            year,
            EventKind.FigureBorn,
            child.Id,
            obj: father.Id,
            location: civilization.CapitalId,
            extra: child.DynastyId.IsNone
                ? new[] { mother.Id }
                : new[] { mother.Id, child.DynastyId });

        if (rng.Chance(ChildbedRisk))
        {
            Houses.Die(world, mother, year, DeathCause.Childbirth);
        }
    }
}
