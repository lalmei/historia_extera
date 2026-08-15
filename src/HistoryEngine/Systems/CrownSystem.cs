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
    public string Name => "crown";

    public void Tick(WorldState world, int year)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            // Faded before the blend, so the year is judged against a memory that has already
            // dimmed by a year rather than against last year's still-fresh one.
            civilization.Fortunes.Fade();
        }
    }
}
