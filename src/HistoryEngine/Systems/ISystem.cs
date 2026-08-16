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

/// <summary>
/// A system that also answers for one kind of scheduled work.
/// </summary>
/// <remarks>
/// <para><b>Why the simulator dispatches rather than each system draining for itself.</b>
/// <see cref="World.Docket.TryTakeDue"/> hands back whatever is due, of any kind — so a system that
/// drained the queue looking for its own work would take everybody else's out of it on the way
/// past. Filtering by kind at the call site would fix that and put the same total order at the
/// mercy of which system happened to ask first. One drainer, dispatching by a declared owner, is
/// the only arrangement in which the queue's order is a property of the queue.</para>
///
/// <para><b>Ownership is declared here, not by cadence.</b> A system is free to keep a clock
/// cadence and also answer for scheduled work — a plague that ignites once a year and steps its
/// outbreaks on their own schedule is exactly that shape, and forcing it to be one or the other
/// would split a model across two systems to satisfy an enum. <see cref="Cadence.Episodic"/> means
/// only that the clock never ticks this system; it says nothing about what wakes it.</para>
///
/// <para>Two systems claiming one kind, or an <see cref="Cadence.Episodic"/> system that implements
/// nothing to be woken by, are both rejected when the simulator is built rather than discovered as
/// a siege that never resolves.</para>
/// </remarks>
public interface IEpisodic
{
    /// <summary>
    /// The kinds of scheduled work this system answers for.
    /// </summary>
    /// <remarks>
    /// A list rather than one kind, because the second consumer wanted two the moment it existed:
    /// a plague both travels to a town and steps the outbreak that sent it, and those are different
    /// work on the same model. Splitting them across two systems to keep this singular would have
    /// put one model's state behind two owners, which is a worse thing than a list.
    /// </remarks>
    IReadOnlyList<DocketKind> Handles { get; }

    /// <summary>
    /// Resolves one entry that has fallen due.
    /// </summary>
    /// <param name="entry">The work, carrying the stamp it was due at.</param>
    /// <param name="now">The step it is being resolved in, which is at or after the due stamp.</param>
    /// <remarks>
    /// <para>Handed one entry at a time rather than a list, because the fork rule for a scheduled
    /// episode is on its own subject's id: a siege's dice must not depend on how many other sieges
    /// were queued before it, and a method given the whole batch would have to be trusted to
    /// remember that.</para>
    ///
    /// <para>Events written here are dated at the entry's own due stamp, not the step's. That is
    /// the whole of what the docket buys — a day reached as a due date rather than by iterating
    /// toward it.</para>
    /// </remarks>
    void Resolve(WorldState world, DocketEntry entry, Stamp now);
}
