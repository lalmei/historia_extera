using HistoryEngine.Core;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Lets the year's recorded hardships reach the people who lived through them.
/// </summary>
/// <remarks>
/// <para>A phase rather than a model. Famine, plague, sack and disaster all write their episodes
/// during the year; this drains what they recorded once the year's journeys are known, which is
/// the first moment the engine knows who was actually at home when it ended.</para>
///
/// <para><b>At the year's close, not its opening.</b> It used to run annually, immediately after
/// <c>travel</c>, on the reasoning that travel was the last thing to send anybody away. The seasonal
/// <c>war</c> made that false: a march levied in autumn is recorded three steps after an opening
/// pass has already decided the soldier was home at year end, so somebody who wintered with the
/// host still carried the town's plague. A sack stormed after the opening step was worse off — it
/// waited for next spring and was remembered under next year's date. Running in the closing season
/// answers both with the same move. The cost is that a famine's dead now die at the end of the year
/// rather than before the figure passes consider them, which is also when a famine kills.</para>
///
/// <para>It draws no dice of its own — every consequence hangs off the episode's own fork inside
/// <see cref="Hardships"/> — so this system's position in the order decides what is known when the
/// consequences land, and nothing else.</para>
/// </remarks>
public sealed class HardshipSystem : ISystem
{
    public string Name => "hardship";

    public Cadence Cadence => Cadence.Closing;

    public void Tick(WorldState world, Stamp now) => Hardships.ResolveYear(world);
}
