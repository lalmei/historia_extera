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
    /// <summary>Chance a war sees a pitched engagement in any given year.</summary>
    private const double CampaignChance = 0.55;

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

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        IRng rng = world.Root.Fork(Name, year);

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

            if (rng.Chance(CampaignChance)) Warfare.Fight(world, war, year, rng);

            if (ShouldSettle(war, year, rng)) Warfare.MakePeace(world, war, year, rng);
        }
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
