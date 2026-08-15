using HistoryEngine.Core;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// How often a system is ticked.
/// </summary>
/// <remarks>
/// <para>Resolution is bought where a decision needs it and nowhere else — the argument
/// <c>TerrainAtlas</c> makes about space, applied to time. Most systems have nothing to say on most
/// days: growth of 3.8% a year divided across 360 days is arithmetic noise with a random number
/// attached, and a diplomatic relation that drifts 6% a year does not want 360 dice where it
/// currently has one. So a uniform finer tick is rejected outright and each system declares what it
/// actually needs.</para>
///
/// <para><b>Seasonal and Episodic are declared before anything uses them.</b> Every system in the
/// engine is <see cref="Annual"/> today, which is the first stage of this clock working as intended
/// rather than a shortcut: the mechanical change lands under a fingerprint proving it moved no
/// history, and the re-phasing lands afterwards where it can be read on its own.</para>
/// </remarks>
public enum Cadence
{
    /// <summary>Once a year. The spine, and where the great majority of this engine belongs.</summary>
    Annual = 0,

    /// <summary>Once a season, for a subject with a rhythm: campaigning, sowing, a pass that closes.</summary>
    Seasonal = 1,

    /// <summary>Only when woken by the docket, so a system with nothing running costs nothing.</summary>
    Episodic = 2,
}

/// <summary>
/// One subsystem of the simulation.
/// </summary>
/// <remarks>
/// <para><b>Ordering is the contract.</b> Systems run in the fixed order declared by
/// <see cref="Simulator.SystemOrder"/>, and that order is folded into the config hash, because
/// reordering two systems changes the resulting history as surely as changing the seed does.
/// A system reads state left behind by the systems before it in the same step.</para>
///
/// <para>Each system draws from its own forked RNG substream rather than a shared stream —
/// see <see cref="Pcg32"/> — so adding a die roll to one system cannot perturb any other.
/// The convention for an annual system is one fork per system per year:
/// <c>world.Root.Fork(Name, year)</c>. Within a step a system iterates entities in id order, so its
/// own draws stay ordered.</para>
///
/// <para>Systems must not mutate state that an earlier system in the same step has already
/// read and acted on. There is no enforcement of that beyond ordering discipline; the escape hatch,
/// if it starts causing bugs, is to buffer intents and apply them in a separate phase — which is
/// what <see cref="World.Docket"/> now is, arriving for a different reason than the one it was
/// written down against.</para>
/// </remarks>
public interface ISystem
{
    /// <summary>
    /// Stable identifier, used both as the RNG fork label and in the config hash. Renaming a
    /// system changes every history it participates in.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// How often this system runs.
    /// </summary>
    /// <remarks>
    /// Declared on every system rather than defaulted, and folded into
    /// <see cref="Simulator.SystemOrderHash"/> alongside the name: changing a system's cadence
    /// changes the history exactly as much as moving it in the order does, so it should be as
    /// visible in a diff and as visible in the run's identity.
    /// </remarks>
    Cadence Cadence { get; }

    /// <summary>Runs one step, at <paramref name="now"/>.</summary>
    /// <remarks>
    /// A system may only stamp events inside the step it is running in. It is a discipline rather
    /// than an enforcement, and it is what keeps the chronicle non-decreasing in <c>(year, day)</c>
    /// once events carry days.
    /// </remarks>
    void Tick(WorldState world, Stamp now);
}
