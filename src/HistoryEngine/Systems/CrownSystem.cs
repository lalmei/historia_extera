using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Settles, once a year, what each realm is actually governed by.
/// </summary>
/// <remarks>
/// <para>Every decision a realm made used to be read straight off its culture, which is fixed at
/// worldgen and never changes — so a warlike people declared war at the same rate in its first
/// century and its ninth, under thirty different rulers, having won every war or lost every one.
/// This system is where a people, the person governing it, and what has lately happened to it are
/// combined into the values the rest of the year is judged against.</para>
///
/// <para><b>Runs first, and writes an answer everything else reads.</b> The same discipline
/// <see cref="Civilization.StateReligionId"/> already follows: computed once and stored, so that a
/// war declared in spring and a colony founded in autumn are judged by the same ruler in the same
/// mood. Recomputing on demand would make a system's answer depend on where in the tick it asked.</para>
///
/// <para>The consequence to accept deliberately: succession runs late in the year, so a ruler
/// crowned this autumn takes effect next spring. That is a year's lag against a thirteen-year mean
/// reign, and it buys the property above.</para>
///
/// <para>Draws no random numbers and samples no terrain.</para>
/// </remarks>
public sealed class CrownSystem : IYearSystem
{
    /// <summary>
    /// The most of the distance to their own inclinations any one person may move a people.
    /// </summary>
    /// <remarks>
    /// A ruler is an argument, not a replacement. Without a ceiling, a long-reigning centralising
    /// chief of a pragmatic people displaces their culture almost entirely, and the culture — which
    /// is the thing that makes two realms recognisably different for eight centuries — stops
    /// meaning anything for the length of one life.
    /// </remarks>
    private const double LatitudeCap = 0.6;

    /// <summary>Years on the throne after which a ruler has bent the realm as far as they will.</summary>
    /// <remarks>
    /// The control on reign-to-reign whiplash. With a thirteen-year mean reign, a realm whose
    /// character re-rolled fully at every coronation would change six times a century, which is
    /// noise rather than history. Starting each ruler near their culture and letting them diverge
    /// as they hold on means a short reign barely registers and a long one is felt.
    /// </remarks>
    private const double SettledReign = 15.0;

    /// <summary>How much less latitude a regent has than a ruler in their own right.</summary>
    /// <remarks>
    /// A regent governs on someone else's behalf and everyone at court knows the arrangement ends.
    /// They still show up in the record — a dowager's decade should read as her decade — but they
    /// move the realm less than a crowned adult would.
    /// </remarks>
    private const double RegentLatitude = 0.6;

    public string Name => "crown";

    public void Tick(WorldState world, int year)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            // Faded before the blend, so the year is judged against a memory that has already
            // dimmed by a year rather than against last year's still-fresh one.
            civilization.Fortunes.Fade();

            civilization.EffectiveValues = Settle(world, civilization, year);
        }
    }

    /// <summary>
    /// The values this realm is governed by for the coming year.
    /// </summary>
    /// <remarks>
    /// Three layers, in order: the people, whoever is governing them, and what has lately
    /// happened to both. An empty throne skips the middle one — an interregnum is governed by
    /// nobody in particular, which is exactly what falling back to the culture says.
    /// </remarks>
    private static CultureValues Settle(WorldState world, Civilization civilization, int year)
    {
        Culture culture = world.CultureOf(civilization);
        CultureValues values = culture.Values;

        Figure? governor = Governing(world, civilization, out bool asRegent);

        if (governor is not null)
        {
            double latitude = Latitude(culture, civilization, governor, year);
            if (asRegent) latitude *= RegentLatitude;

            values = values.BlendToward(governor.Disposition.Values, latitude);
        }

        return values.ShiftedBy(civilization.Fortunes);
    }

    /// <summary>Whoever actually governs: the regent while one sits, otherwise the ruler.</summary>
    private static Figure? Governing(
        WorldState world, Civilization civilization, out bool asRegent)
    {
        asRegent = false;

        if (world.Figures.Contains(civilization.RegentId))
        {
            Figure regent = world.Figures[civilization.RegentId];
            if (regent.IsAlive)
            {
                asRegent = true;
                return regent;
            }
        }

        if (world.Figures.Contains(civilization.CurrentRulerId))
        {
            Figure ruler = world.Figures[civilization.CurrentRulerId];
            if (ruler.IsAlive) return ruler;
        }

        return null;
    }

    /// <summary>
    /// How far this person may move this people, in [0, <see cref="LatitudeCap"/>].
    /// </summary>
    /// <remarks>
    /// Government form sets the basis, because it decides what instruments a ruler has: a chief
    /// decides by being obeyed and a consul is fighting his own constitution. Tradition brakes it,
    /// tenure earns it, and <see cref="Disposition.Centralism"/> is how hard this particular
    /// person insists — which is the second consumer of that dial and what keeps it honest.
    /// </remarks>
    private static double Latitude(
        Culture culture, Civilization civilization, Figure governor, int year)
    {
        double basis = culture.Government switch
        {
            GovernmentForm.Chiefdom => 0.45,
            GovernmentForm.Monarchy => 0.40,
            GovernmentForm.Theocracy => 0.35,
            GovernmentForm.Oligarchy => 0.25,
            GovernmentForm.Republic => 0.20,
            _ => 0.30,
        };

        double brake = DetMath.Lerp(1.25, 0.65, culture.Values.Tradition);

        int held = Math.Max(0, year - civilization.RulerSinceYear);
        double tenure = DetMath.Lerp(
            0.50, 1.00, DetMath.Clamp01(held / SettledReign));

        double insistence = DetMath.Lerp(0.75, 1.25, governor.Disposition.Centralism);

        return Math.Min(LatitudeCap, basis * brake * tenure * insistence);
    }
}
