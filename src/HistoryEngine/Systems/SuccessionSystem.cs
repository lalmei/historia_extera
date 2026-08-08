using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Fills empty thrones: heirs, elections, regencies, disputes, and the failure of houses.
/// </summary>
/// <remarks>
/// <para>Runs immediately after <see cref="FigureLifecycleSystem"/> and depends on that ordering —
/// a ruler who dies this year is replaced this year, so a chronicle never contains an unexplained
/// gap in a line. That dependency is why <see cref="Simulator.SystemOrder"/> is part of the run's
/// identity.</para>
///
/// <para><b>Three ways a throne changes hands</b>, and all three had to exist before any of them
/// read as history. An heir inherits it, which is the ordinary case. A house fails to produce one
/// and the crown passes to a house that married into the realm — or, when there is nobody at all,
/// to somebody the chronicle had not been following, which is the pre-dynastic behaviour demoted
/// to the exception it always was. And in a realm that elects its rulers the office simply falls
/// vacant on schedule, which is what makes a republic's chronicle look nothing like a
/// monarchy's.</para>
/// </remarks>
public sealed class SuccessionSystem : IYearSystem
{
    /// <summary>Chance of a contested succession at zero aggression, before the culture's own is added.</summary>
    private const double DisputeFloor = 0.06;

    /// <summary>Additional dispute chance at full <see cref="CultureValues.Aggression"/>.</summary>
    private const double DisputeFromAggression = 0.28;

    /// <summary>Odds the presumed successor still carries a disputed succession.</summary>
    private const double StrongerClaimPrevails = 0.7;

    /// <summary>Odds the losing claimant does not survive having lost.</summary>
    private const double LoserIsExecuted = 0.35;

    /// <summary>Relative weight of each place on an elective ballot, strongest claim first.</summary>
    private static readonly double[] BallotWeights = { 0.45, 0.27, 0.17, 0.11 };

    public string Name => "succession";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);

            // A coronation names the place it happened, so a realm whose seat has been abandoned
            // needs a new one before anyone can be crowned there.
            EnsureCapital(world, civilization, year);
            VacateIfDead(world, civilization);

            if (!civilization.CurrentRulerId.IsNone)
            {
                if (!TermHasRun(civilization, culture, year))
                {
                    ReviewRegency(world, civilization, year);
                    continue;
                }

                StandDown(world, civilization, culture, year);
            }

            Crown(world, civilization, culture, year, rng);
        }
    }

    /// <summary>
    /// Empties a throne whose holder is dead, however that came to be missed.
    /// </summary>
    /// <remarks>
    /// A death normally vacates its own throne. This is the belt to that braces, and it is here
    /// because the failure mode is silent and permanent rather than loud: a realm whose ruler is
    /// dead but still recorded is simply skipped every year afterwards, so it holds no more
    /// successions and its chronicle quietly stops — which reads as a realm that happened to have
    /// nothing happen for two centuries, not as a bug.
    /// </remarks>
    private static void VacateIfDead(WorldState world, Civilization civilization)
    {
        if (world.Figures.Contains(civilization.CurrentRulerId)
            && !world.Figures[civilization.CurrentRulerId].IsAlive)
        {
            civilization.CurrentRulerId = EntityId.None;
        }

        if (world.Figures.Contains(civilization.RegentId)
            && !world.Figures[civilization.RegentId].IsAlive)
        {
            civilization.RegentId = EntityId.None;
        }
    }

    private static bool TermHasRun(Civilization civilization, Culture culture, int year) =>
        culture.TermYears > 0 && year - civilization.RulerSinceYear >= culture.TermYears;

    /// <summary>Ends a fixed term. The office falls vacant with its holder still alive.</summary>
    private static void StandDown(
        WorldState world, Civilization civilization, Culture culture, int year)
    {
        Figure ruler = world.Figures[civilization.CurrentRulerId];
        ruler.EndCurrentTitle(year);

        civilization.CurrentRulerId = EntityId.None;
        civilization.RegentId = EntityId.None;

        world.Chronicle.Record(
            year,
            EventKind.RulerTermEnded,
            ruler.Id,
            obj: civilization.Id,
            data: Chronicle.Data(
                ("title", culture.RulerTitle),
                ("years", (year - civilization.RulerSinceYear).ToString(CultureInfo.InvariantCulture))));
    }

    private static void Crown(
        WorldState world, Civilization civilization, Culture culture, int year, IRng rng)
    {
        EntityId outgoing = civilization.RulerIds.Count > 0
            ? civilization.RulerIds[civilization.RulerIds.Count - 1]
            : EntityId.None;

        List<Figure> line = Succession.Claimants(world, civilization, culture, outgoing);

        if (line.Count == 0)
        {
            CrownWithoutAnHeir(world, civilization, culture, year, outgoing, rng);
            return;
        }

        Figure presumed = culture.Succession == SuccessionLaw.Elective
            ? Elect(line, rng)
            : line[0];

        Figure heir = Contest(
            world, civilization, culture, line, presumed, year, rng, out string claim);

        Houses.Enthrone(world, civilization, culture, heir, year, claim);
        ReviewRegency(world, civilization, year);
    }

    /// <summary>
    /// Gives the strongest passed-over claimant a chance to press their case.
    /// </summary>
    /// <remarks>
    /// <para>Without this a line of succession is a queue and every reign begins the same way.
    /// A contested one is where <see cref="CultureValues.Aggression"/> earns its keep outside the
    /// war systems: an aggressive people settles its successions the way it settles everything
    /// else, and its chronicle carries the executed brothers to show for it.</para>
    ///
    /// <para>Elections are contested on the same terms as inheritances, and for the same reason —
    /// a ballot that passes over the man with the best claim has made an enemy exactly as an
    /// inheritance would. It is also what keeps the elective realms, which hold far the most
    /// successions, from being the placid ones.</para>
    /// </remarks>
    private static Figure Contest(
        WorldState world,
        Civilization civilization,
        Culture culture,
        List<Figure> line,
        Figure presumed,
        int year,
        IRng rng,
        out string claim)
    {
        claim = ClaimLabel(culture.Succession);

        Figure? rival = null;
        for (int i = 0; i < line.Count; i++)
        {
            if (line[i].Id == presumed.Id) continue;

            rival = line[i];
            break;
        }

        if (rival is null) return presumed;

        double chance = DisputeFloor + DisputeFromAggression * culture.Values.Aggression;
        if (!rng.Chance(chance)) return presumed;

        bool presumedWins = rng.Chance(StrongerClaimPrevails);
        Figure winner = presumedWins ? presumed : rival;
        Figure loser = presumedWins ? rival : presumed;

        world.Chronicle.Record(
            year,
            EventKind.SuccessionDisputed,
            winner.Id,
            obj: loser.Id,
            location: civilization.CapitalId);

        if (rng.Chance(LoserIsExecuted))
        {
            Houses.Die(world, loser, year, DeathCause.Execution);
        }

        claim = "after a disputed succession";
        return winner;
    }

    /// <summary>Weighted draw across the strongest claims, so an election is not simply the heir.</summary>
    private static Figure Elect(List<Figure> line, IRng rng)
    {
        int pool = Math.Min(Succession.ElectorateSize, line.Count);

        double total = 0.0;
        for (int i = 0; i < pool; i++) total += BallotWeights[i];

        double roll = rng.NextDouble() * total;

        for (int i = 0; i < pool; i++)
        {
            roll -= BallotWeights[i];
            if (roll < 0.0) return line[i];
        }

        return line[pool - 1];
    }

    /// <summary>
    /// The crown when the ruling house cannot supply a successor.
    /// </summary>
    /// <remarks>
    /// <para>Reaching here does not mean the house is extinct, and the two must not be conflated.
    /// An agnatic house reduced to daughters has plenty of living members and still cannot produce
    /// a king; those daughters go on to carry their father's claim into another house's nursery.
    /// Extinction proper is detected at the graveside, in <see cref="Houses.Die"/>.</para>
    ///
    /// <para>The crown is offered outward in order of how much the realm already knows the person:
    /// the late ruler's widowed consort, then any house that has settled here, and only then a
    /// stranger — which is the pre-dynastic behaviour, kept as the last resort it always was.</para>
    /// </remarks>
    private static void CrownWithoutAnHeir(
        WorldState world,
        Civilization civilization,
        Culture culture,
        int year,
        EntityId outgoing,
        IRng rng)
    {
        // The house has failed, but the realm's law has not. An agnatic people that would not
        // crown a daughter of its own house will not crown a stranger's either.
        bool Acceptable(Figure candidate) =>
            candidate.IsAlive
            && candidate.AgeIn(year) >= Succession.MajorityAge
            && !Succession.HoldsAThrone(world, candidate)
            && (culture.Succession != SuccessionLaw.Agnatic || candidate.Sex == Sex.Male);

        Figure? successor = ConsortOf(world, outgoing, Acceptable)
            ?? ResidentOutsider(world, civilization, Acceptable, rng);

        // Whoever it turns out to be, the crown is not handed to a house already ruling next door
        // when the realm has anyone else — the same balance an elective ballot strikes.
        if (successor is not null && Succession.RulesElsewhere(world, successor, civilization))
        {
            successor = ResidentOutsider(
                world,
                civilization,
                candidate => Acceptable(candidate)
                    && !Succession.RulesElsewhere(world, candidate, civilization),
                rng) ?? successor;
        }

        string claim = successor is not null
            ? "by right of marriage into the house"
            : "at the founding of a new house";

        successor ??= Houses.FoundDynasty(world, civilization, culture, year, rng);

        Houses.Enthrone(world, civilization, culture, successor, year, claim);
        ReviewRegency(world, civilization, year);
    }

    /// <summary>
    /// The last ruler's surviving consort, if they bring a house of their own.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="Figure.SpouseIds"/> rather than <see cref="Figure.SpouseId"/>, since a
    /// death clears the live link on both sides — the marriage is over, but the widow's claim to
    /// have been married to a king is not.
    /// </remarks>
    private static Figure? ConsortOf(
        WorldState world, EntityId rulerId, Func<Figure, bool> acceptable)
    {
        if (!world.Figures.Contains(rulerId)) return null;

        Figure ruler = world.Figures[rulerId];

        for (int i = ruler.SpouseIds.Count - 1; i >= 0; i--)
        {
            Figure consort = world.Figures[ruler.SpouseIds[i]];

            if (consort.DynastyId.IsNone || !acceptable(consort)) continue;

            return consort;
        }

        return null;
    }

    /// <summary>An adult of some other house already living in the realm, if there is one.</summary>
    private static Figure? ResidentOutsider(
        WorldState world, Civilization civilization, Func<Figure, bool> acceptable, IRng rng)
    {
        var candidates = new List<Figure>();

        foreach (Dynasty house in world.Dynasties)
        {
            if (house.Id == civilization.RulingDynastyId) continue;

            foreach (EntityId id in house.MemberIds)
            {
                Figure member = world.Figures[id];

                if (member.CivilizationId != civilization.Id || !acceptable(member)) continue;

                candidates.Add(member);
            }
        }

        return candidates.Count == 0 ? null : rng.Pick(candidates);
    }

    /// <summary>Begins, transfers or ends a regency, according to the ruler's age.</summary>
    private static void ReviewRegency(WorldState world, Civilization civilization, int year)
    {
        if (civilization.CurrentRulerId.IsNone) return;

        Figure ruler = world.Figures[civilization.CurrentRulerId];

        if (ruler.AgeIn(year) >= Succession.MajorityAge)
        {
            if (civilization.RegentId.IsNone) return;

            if (world.Figures.Contains(civilization.RegentId))
            {
                world.Figures[civilization.RegentId].EndCurrentTitle(year);
            }

            civilization.RegentId = EntityId.None;

            world.Chronicle.Record(
                year, EventKind.RegencyEnded, ruler.Id, obj: civilization.Id);
            return;
        }

        bool standing = world.Figures.Contains(civilization.RegentId)
            && world.Figures[civilization.RegentId].IsAlive;

        if (standing) return;

        Figure? regent = ChooseRegent(world, civilization, ruler, year);
        if (regent is null) return;

        civilization.RegentId = regent.Id;
        regent.Titles.Add(new TitleHolding("Regent", civilization.Id, year, null));

        world.Chronicle.Record(
            year,
            EventKind.RegencyBegan,
            regent.Id,
            obj: ruler.Id,
            location: civilization.CapitalId,
            data: Chronicle.Data(("age", Chronicle.Years(ruler.AgeIn(year)))));
    }

    /// <summary>
    /// Who governs for a child ruler: their mother first, then the eldest of the house.
    /// </summary>
    /// <remarks>
    /// The mother is preferred because she is the one person whose interest in the child's reign
    /// is not also a claim to replace it — which is exactly the reasoning that produced so many
    /// historical queen regents.
    /// </remarks>
    private static Figure? ChooseRegent(
        WorldState world, Civilization civilization, Figure ruler, int year)
    {
        bool Eligible(Figure candidate) =>
            candidate.IsAlive
            && candidate.Id != ruler.Id
            && candidate.AgeIn(year) >= Succession.MajorityAge
            && !Succession.HoldsAThrone(world, candidate);

        foreach (EntityId parentId in ruler.Parents())
        {
            Figure parent = world.Figures[parentId];
            if (Eligible(parent)) return parent;
        }

        Figure? eldest = null;
        foreach (Figure relative in Succession.Kin(world, civilization))
        {
            if (!Eligible(relative)) continue;

            if (eldest is null
                || relative.BirthYear < eldest.BirthYear
                || (relative.BirthYear == eldest.BirthYear && relative.Id.CompareTo(eldest.Id) < 0))
            {
                eldest = relative;
            }
        }

        return eldest;
    }

    /// <summary>Repoints a civilization's capital if the current one is gone.</summary>
    private static void EnsureCapital(WorldState world, Civilization civilization, int year)
    {
        bool capitalStands = !civilization.CapitalId.IsNone
            && world.Settlements[civilization.CapitalId].IsActive;

        if (capitalStands) return;

        Settlement? replacement = null;
        foreach (Settlement candidate in world.ActiveSettlementsOf(civilization))
        {
            // Largest surviving settlement, id breaking ties.
            if (replacement is null
                || candidate.Population > replacement.Population
                || (candidate.Population == replacement.Population
                    && candidate.Id.CompareTo(replacement.Id) < 0))
            {
                replacement = candidate;
            }
        }

        if (replacement is null) return;

        if (!civilization.CapitalId.IsNone && world.Settlements.Contains(civilization.CapitalId))
        {
            world.Settlements[civilization.CapitalId].IsCapital = false;
        }

        replacement.IsCapital = true;
        civilization.CapitalId = replacement.Id;

        world.Chronicle.Record(
            year, EventKind.CapitalMoved, civilization.Id, location: replacement.Id);
    }

    private static string ClaimLabel(SuccessionLaw law) => law switch
    {
        SuccessionLaw.Agnatic => "by right of birth in the male line",
        SuccessionLaw.Seniority => "as the eldest of the house",
        SuccessionLaw.Elective => "by the election of the realm",
        _ => "by right of birth",
    };
}
