using HistoryEngine.Core;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Moves craftsmen up the ladder inside their trades.
/// </summary>
/// <remarks>
/// <para><b>The third of the career systems.</b> <see cref="OfficeSystem"/> fills the seats a court
/// decides and <see cref="MilitarySystem"/> the rungs an army decides; this one fills the stages a
/// company decides, which is the career the largest number of recorded figures actually have. Of
/// everyone who enters a trade, almost nobody is ever elected to a mastery seat — before this the
/// rest had no working history at all beyond the year they were bound.</para>
///
/// <para><b>Runs after the offices</b>, so a master elected to a mastery this spring is already at
/// the top of his ladder when his company's shops are counted, and the journeyman who would
/// otherwise have been admitted into that place waits. The reverse order gives a company one more
/// master than it has room for in every year it elects one.</para>
///
/// <para><b>And before the artifacts</b>, which is the point of the ordering rather than a detail:
/// <see cref="World.Makers"/> names a master on an object and nobody else, so a man admitted this
/// year can be named on a thing made this year. A work is credited to the standing its maker held
/// when he made it.</para>
///
/// <para>Thin on purpose. The census and the decisions are <see cref="Grades.Advance"/>, in
/// <c>World/</c>, for the reason <see cref="Guilds.Fill"/> is: a pass that has to bucket a world's
/// figures by town and trade wants a keyed lookup, and <c>Systems/</c> is held to ordered
/// collections.</para>
///
/// <para>Samples no terrain.</para>
/// </remarks>
public sealed class CraftSystem : ISystem
{
    public string Name => "grades";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now) =>
        Grades.Advance(world, now.Year, world.Root.Fork(Name, now.Year));
}
