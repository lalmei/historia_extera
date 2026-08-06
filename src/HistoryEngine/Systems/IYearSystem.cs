using HistoryEngine.Core;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// One subsystem of the simulation, ticked once per year.
/// </summary>
/// <remarks>
/// <para><b>Ordering is the contract.</b> Systems run in the fixed order declared by
/// <see cref="Simulator.SystemOrder"/>, and that order is folded into the config hash, because
/// reordering two systems changes the resulting history as surely as changing the seed does.
/// A system reads state left behind by the systems before it in the same year.</para>
///
/// <para>Each system draws from its own forked RNG substream rather than a shared stream —
/// see <see cref="Pcg32"/> — so adding a die roll to one system cannot perturb any other.
/// The convention is one fork per system per year: <c>world.Root.Fork(Name, year)</c>. Within
/// a year a system iterates entities in id order, so its own draws stay ordered.</para>
///
/// <para>Systems must not mutate state that an earlier system in the same year has already
/// read and acted on. There is no enforcement of that beyond ordering discipline; if it starts
/// causing bugs, the fix is to buffer intents and apply them in a separate phase, noted in
/// DESIGN.md as the escape hatch.</para>
/// </remarks>
public interface IYearSystem
{
    /// <summary>
    /// Stable identifier, used both as the RNG fork label and in the config hash. Renaming a
    /// system changes every history it participates in.
    /// </summary>
    string Name { get; }

    void Tick(WorldState world, int year);
}
