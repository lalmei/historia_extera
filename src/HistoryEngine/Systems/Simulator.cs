using HistoryEngine.Core;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Runs the yearly tick loop.
/// </summary>
/// <remarks>
/// <para><b>Strictly sequential, deliberately.</b> There is no parallelism in the tick loop and
/// there should not be: the cost of making a parallel simulation deterministic — collecting
/// per-thread results and applying them in a fixed order — exceeds the benefit at this scale,
/// where a few centuries across a dozen civilizations runs in well under a second. If a future
/// milestone genuinely needs it, the pattern is collect-then-apply, never parallel mutation.</para>
///
/// <para><b>System order is part of the determinism contract.</b> <see cref="SystemOrder"/> is
/// folded into the run's identity, because swapping two systems changes the resulting history
/// exactly as much as changing the seed does — population growth before promotion produces a
/// different chronicle than promotion before growth.</para>
/// </remarks>
public sealed class Simulator
{
    private readonly IReadOnlyList<IYearSystem> _systems;

    public Simulator(IReadOnlyList<IYearSystem>? systems = null) =>
        _systems = systems ?? DefaultSystems();

    /// <summary>
    /// The Milestone 1 system order.
    /// </summary>
    /// <remarks>
    /// Reads as a causal chain within each year: populations change, that changes what
    /// settlements are, pressure from that drives expansion, and rulers live and die
    /// independently of all of it. Milestones 6 and 8 insert diplomacy, war and flavour systems
    /// into this list.
    /// </remarks>
    public static IReadOnlyList<IYearSystem> DefaultSystems() => new IYearSystem[]
    {
        new PopulationSystem(),
        new SettlementLifecycleSystem(),
        new ExpansionSystem(),
        new FigureLifecycleSystem(),
    };

    public IReadOnlyList<IYearSystem> Systems => _systems;

    /// <summary>The system names in execution order. Contributes to a run's identity.</summary>
    public IReadOnlyList<string> SystemOrder
    {
        get
        {
            var names = new string[_systems.Count];
            for (int i = 0; i < _systems.Count; i++) names[i] = _systems[i].Name;
            return names;
        }
    }

    /// <summary>A stable hash of the system list and its order.</summary>
    public string SystemOrderHash
    {
        get
        {
            ulong hash = Hash.OfString("systems");
            foreach (string name in SystemOrder)
            {
                hash = Hash.Combine(hash, Hash.OfString(name));
            }

            return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Simulates from the world's current year through to the configured end.</summary>
    public void Run(WorldState world)
    {
        for (int year = world.Year; year <= world.EndYear; year++)
        {
            Tick(world, year);
        }
    }

    /// <summary>
    /// Advances by <paramref name="years"/>.
    /// </summary>
    /// <remarks>
    /// Exists so tests can assert that a run split across two calls produces the same history as
    /// one continuous run — the strongest determinism test available, since any state that has
    /// leaked outside <see cref="WorldState"/> shows up as a divergence.
    /// </remarks>
    public void Advance(WorldState world, int years)
    {
        int last = Math.Min(world.Year + years - 1, world.EndYear);
        for (int year = world.Year; year <= last; year++)
        {
            Tick(world, year);
        }
    }

    private void Tick(WorldState world, int year)
    {
        world.Year = year;

        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].Tick(world, year);
        }

        // Leave the clock on the next year to simulate, so Run and Advance can resume.
        world.Year = year + 1;
    }
}
