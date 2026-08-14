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
    /// The system order as of the persistent trade-route model.
    /// </summary>
    /// <remarks>
    /// <para>Reads as a causal chain within each year: populations change against the harvest,
    /// pestilence and the land itself take their share of what is left, that changes what
    /// settlements are, a settlement that has outgrown a hamlet acquires a character, pressure
    /// drives expansion, expansion moves borders, faiths travel across the borders as they now
    /// stand, neighbours judge each other by both, the wars that follow from that are fought,
    /// trade links respond to the resulting peace or war, personal hazards and biological mortality
    /// take their turns, thrones left empty by those deaths are filled, the houses go
    /// on, and what the year's survivors made is written down.</para>
    ///
    /// <para>Plague and disaster follow population rather than preceding it, so a year's growth
    /// is applied to a settlement before the year's mortality takes from it — the other order
    /// lets a town regrow inside the same tick that emptied it, and the plague reads as half the
    /// size it was. They precede the lifecycle so that a settlement gutted this year is judged
    /// this year, which is what makes a plague able to finish a place.</para>
    ///
    /// <para>Religion sits between expansion and diplomacy for the same reason diplomacy sits
    /// after expansion: an opinion should be formed about the frontier and the faith that exist
    /// now, not last year's. It also means a province taken in a spring campaign is judged by its
    /// new owner's faith in the same year it changed hands.</para>
    ///
    /// <para>Artifacts run last, after the houses: a crown made in the reign of a ruler crowned
    /// this spring belongs to them and not to whoever the year opened with.</para>
    ///
    /// <para>Specialization sits after lifecycle so a settlement promoted this year can acquire its
    /// character in the same year, and the two events read consecutively in the chronicle. It
    /// therefore feeds capacity from the following year onward, which avoids a same-year dependency
    /// loop between what a settlement is and how many people it supports.</para>
    ///
    /// <para>Diplomacy follows expansion so that an opinion is formed about the frontier that
    /// exists rather than last year's, and war follows diplomacy so a war declared this spring is
    /// fought this summer instead of waiting a year for anything to happen.</para>
    ///
    /// <para>Trade routes follow war so a corridor opens or closes against the peace that actually
    /// survives the year's campaigning, and artifacts run later so books can use that route in the
    /// year it is established.</para>
    ///
    /// <para>The figure-lifecycle, succession and household sequence is the tightest coupling in
    /// the list, and war leans on the same property. Deaths must precede succession or a realm
    /// spends a year without a ruler for no
    /// reason the chronicle can explain — which is as true of a king killed at a siege as of one
    /// who died in bed — and succession must precede the houses or a new king's brothers are still
    /// ranked as heirs on the day he is crowned, and marry accordingly.</para>
    /// </remarks>
    public static IReadOnlyList<IYearSystem> DefaultSystems() => new IYearSystem[]
    {
        new PopulationSystem(),
        new PlagueSystem(),
        new DisasterSystem(),
        new SettlementLifecycleSystem(),
        new SpecializationSystem(),
        new ExpansionSystem(),
        new ReligionSystem(),
        new DiplomacySystem(),
        new WarSystem(),
        new TradeRouteSystem(),
        new FigureIncidentSystem(),
        new FigureLifecycleSystem(),
        new SuccessionSystem(),
        new HouseholdSystem(),
        new ArtifactSystem(),
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
