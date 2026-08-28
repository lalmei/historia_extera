using HistoryEngine.Core;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Lets the year's recorded hardships reach the people who lived through them.
/// </summary>
/// <remarks>
/// <para>A phase rather than a model. Famine, plague, sack and disaster all write their episodes
/// near the top of the year; this drains what they recorded once <c>travel</c> has run, which is
/// the first moment the engine knows who was actually at home. Placed immediately after it and
/// before the figure passes, so that somebody a famine killed is dead before the year considers
/// them for an office or a marriage.</para>
///
/// <para>It draws no dice of its own — every consequence hangs off the episode's own fork inside
/// <see cref="Hardships"/> — so this system's position in the order decides what is known when the
/// consequences land, and nothing else.</para>
/// </remarks>
public sealed class HardshipSystem : ISystem
{
    public string Name => "hardship";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now) => Hardships.ResolveYear(world, now.Year);
}
