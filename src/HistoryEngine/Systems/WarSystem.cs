using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Prosecutes the wars diplomacy declared: campaigns, and the peace that ends them.
/// </summary>
/// <remarks>
/// <para>Deliberately small. Everything that changes the world lives in <see cref="Warfare"/>;
/// what is left here is the two decisions a year of war actually consists of — whether an army
/// takes the field, and whether either side has had enough.</para>
///
/// <para><b>Runs before the figure lifecycle, and that ordering is load-bearing.</b> A ruler
/// killed at a battle must be dead before the succession system runs in the same year, or the
/// realm spends a year with an empty throne for no reason the chronicle can explain — the same
/// dependency Milestone 5 established between death and succession, arriving from a new
/// direction.</para>
///
/// <para><b>Quiet years are the point.</b> A battle every year of every war makes a twelve-year
/// war a list of twelve engagements and gives the reader nothing to distinguish one war from
/// another. At roughly one year in two, wars produce campaigns with lulls in them, and the long
/// grinding ones stand out from the short decisive ones.</para>
/// </remarks>
public sealed class WarSystem : ISystem
{
    /// <summary>
    /// Chance a war sees a pitched engagement in any one open season.
    /// </summary>
    /// <remarks>
    /// <para>Derived from the 0.55 a year this used to be, rather than chosen afresh: a war whose
    /// ground is open all four seasons should still see about the same number of engagements it saw
    /// when the year was the tick. That is <c>1 − (1 − 0.55)^¼ = 0.1809</c>, written out as a
    /// literal because the engine will not evaluate a transcendental on a decision path — and
    /// because a constant whose derivation is in a comment is easier to check than a call.</para>
    ///
    /// <para>What changes is the north. A realm whose winter closes loses a quarter of its
    /// campaigning year, and fights measurably less than an equatorial one for the first time. That
    /// asymmetry is the point of the milestone rather than a side effect of it.</para>
    /// </remarks>
    private const double CampaignChance = 0.1809;

    /// <summary>
    /// Years a war must run before either side will treat.
    /// </summary>
    /// <remarks>
    /// Without it a war that opens with one decisive siege ends the year it began, and a realm can
    /// take a province in a single season. Two years is short enough that a lopsided war is still
    /// short and long enough that a war is an era rather than an incident.
    /// </remarks>
    private const int MinimumWarYears = 2;

    /// <summary>Yearly chance of a settlement once one side can dictate terms.</summary>
    /// <remarks>
    /// Not a certainty, because the year a war becomes winnable is rarely the year it stops. The
    /// gap between the two is where a losing realm gets its counter-offensive, and it is what lets
    /// <see cref="War.Score"/> run high enough for a peace to cost more than one region.
    /// </remarks>
    private const double SettlementChance = 0.45;

    /// <summary>
    /// How much likelier a settlement becomes with each further year of fighting.
    /// </summary>
    /// <remarks>
    /// <para>Wars end two ways and both must be available. A decisive one ends because somebody
    /// won; an indecisive one ends because both sides have had enough, and that second route was
    /// missing at first — leaving only a hard cap, which is not the same thing at all. A cap
    /// produces a chronicle in which the indecisive wars all last exactly the cap, and on seed 42
    /// that was most of them.</para>
    ///
    /// <para>A ramp gives the spread instead: at three and a half points a year, half of undecided
    /// wars are settled inside a decade and a twenty-year war is a rarity worth remarking on.</para>
    /// </remarks>
    private const double ExhaustionRate = 0.035;

    /// <summary>Years after which a war ends however it stands.</summary>
    /// <remarks>
    /// A backstop rather than a mechanism, and it exists for a case the exhaustion ramp cannot
    /// reach on its own: two realms that cannot get at each other fight no battles, so nothing
    /// about their war changes from year to year.
    /// </remarks>
    private const int HardCap = 40;

    public string Name => "war";

    public Cadence Cadence => Cadence.Seasonal;

    /// <summary>
    /// Campaigns are fought by the season; terms are agreed once a year.
    /// </summary>
    /// <remarks>
    /// <para><b>The split is the model, not a way of preserving arithmetic.</b> Taking the field is
    /// exactly the decision a season governs — it is what the closed season closes — while whether
    /// a realm has had enough of a war is a judgement about a year of it, and asking it four times
    /// would end wars four times as readily for no reason anybody in them would recognise. Peace is
    /// therefore settled in the closing season, between campaigns, which is also when it was
    /// historically agreed.</para>
    ///
    /// <para>The stream forks on the absolute season rather than the year, per the fork rule for a
    /// seasonal system: monotone, unique, and independent of how many steps any other system took.
    /// An annual system's <c>Fork(Name, year)</c> is untouched, so nothing that kept its cadence
    /// drew a different number because this one changed.</para>
    /// </remarks>
    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;
        Calendar calendar = world.Config.Calendar;

        int season = now.Day / calendar.DaysPerSeason;
        bool closing = season == calendar.SeasonsPerYear - 1;

        IRng rng = world.Root.Fork(Name, calendar.AbsoluteDay(now) / calendar.DaysPerSeason);

        // Collected first: making peace does not change the war table, but ending one can end a
        // civilization, and iterating the table while a fall reshapes territory is asking for it.
        var running = new List<War>();
        foreach (War war in world.ActiveWars()) running.Add(war);

        foreach (War war in running)
        {
            if (Abandoned(world, war))
            {
                Warfare.MakePeace(world, war, year, rng);
                continue;
            }

            if (InSeason(world, war, season) && rng.Chance(CampaignChance))
            {
                Warfare.Fight(world, war, year, rng);
            }

            if (closing && ShouldSettle(war, year, rng)) Warfare.MakePeace(world, war, year, rng);
        }
    }

    /// <summary>
    /// Whether this war's ground is open to an army this season.
    /// </summary>
    /// <remarks>
    /// <para>Read from the defender's seat, because that is the ground campaigned over: an army
    /// marches into the territory it is trying to take, and its own winter is not what stops it. A
    /// war whose defender cannot be located leaves the season open rather than closed — a war that
    /// silently stopped being fought because a lookup failed is far worse than one fought in the
    /// snow.</para>
    ///
    /// <para>The first defender rather than a poll of the coalition. A coalition's members are
    /// usually neighbours, the answer is the same for all of them in the great majority of cases,
    /// and a war fought across two hemispheres is a curiosity this does not need to resolve
    /// correctly to be worth having.</para>
    /// </remarks>
    private static bool InSeason(WorldState world, War war, int season)
    {
        if (war.Defenders.Count == 0) return true;

        EntityId defenderId = war.Defenders[0];
        if (!world.Civilizations.Contains(defenderId)) return true;

        Civilization defender = world.Civilizations[defenderId];
        if (!world.Settlements.Contains(defender.CapitalId)) return true;

        EntityId regionId = world.Settlements[defender.CapitalId].RegionId;
        if (!world.Regions.Contains(regionId)) return true;

        Calendar calendar = world.Config.Calendar;

        return Seasons.Campaigning(
            world.Regions[regionId], season, calendar.SeasonsPerYear, world.Config.WorldSize);
    }

    /// <summary>
    /// True when there is no longer anyone on one side able to fight.
    /// </summary>
    /// <remarks>
    /// A belligerent can fall to famine while its war is running, and a war whose defender no
    /// longer exists would otherwise sit in the table for ever, fighting no battles and blocking
    /// every other realm's declarations against its surviving members.
    /// </remarks>
    private static bool Abandoned(WorldState world, War war)
    {
        return !AnyStanding(war.Attackers) || !AnyStanding(war.Defenders);

        bool AnyStanding(IReadOnlyList<EntityId> coalition)
        {
            foreach (EntityId id in coalition)
            {
                if (world.Civilizations[id].IsActive) return true;
            }

            return false;
        }
    }

    private static bool ShouldSettle(War war, int year, IRng rng)
    {
        int fought = war.YearsIn(year);

        if (fought < MinimumWarYears) return false;
        if (fought >= HardCap) return true;

        double chance = ExhaustionRate * (fought - MinimumWarYears);
        if (Math.Abs(war.Score) >= Warfare.DecisiveScore) chance += SettlementChance;

        return rng.Chance(chance);
    }
}
