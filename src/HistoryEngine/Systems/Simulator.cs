using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Runs the tick loop.
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
///
/// <para><b>One step per year, and every system <see cref="Cadence.Annual"/>.</b> The loop below is
/// still a year loop, which is the point of building the clock before anything runs on it: the
/// stamp, the calendar and the docket are all in place and reviewable, and the fingerprint proves
/// no history moved. Finer cadences change this loop and nothing else about the shape of the
/// engine.</para>
/// </remarks>
public sealed class Simulator
{
    private readonly IReadOnlyList<ISystem> _systems;

    public Simulator(IReadOnlyList<ISystem>? systems = null) =>
        _systems = systems ?? DefaultSystems();

    /// <summary>
    /// The system order as of the persistent trade-route model.
    /// </summary>
    /// <remarks>
    /// <para>The crown settles first: each realm's fortunes fade by a year and the values it will
    /// be governed by are fixed before any system reads them, so every judgement made within one
    /// year is made against the same ruler in the same mood.</para>
    ///
    /// <para>Then the chain reads causally within each year: populations change against the harvest,
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
    ///
    /// <para><b>Every system here is annual.</b> Several of them are described above as if the year
    /// had parts — "a war declared this spring is fought this summer", "a province taken in a
    /// spring campaign", "a crown made in the reign of a ruler crowned this spring". None of those
    /// springs exists yet: they are what the ordering <em>means</em>, written down before there was
    /// a calendar to say it with. Giving <c>crown</c>, <c>war</c> and <c>expansion</c> their seasons
    /// is a separate change, staged on its own so its calibration can be read without a mechanical
    /// refactor underneath it.</para>
    /// </remarks>
    public static IReadOnlyList<ISystem> DefaultSystems() => new ISystem[]
    {
        new CrownSystem(),
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
        new OfficeSystem(),
        new ArtifactSystem(),
    };

    public IReadOnlyList<ISystem> Systems => _systems;

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

    /// <summary>
    /// A stable hash of the system list, its order, and how often each of them runs.
    /// </summary>
    /// <remarks>
    /// <para><b>Cadence belongs in here alongside the name.</b> This hash exists because reordering
    /// two systems changes the history as much as changing the seed does, and changing a system's
    /// cadence does exactly the same thing — a war system rolling four times a year is not the same
    /// engine as one rolling once, whatever the list order says.</para>
    ///
    /// <para>Folded in only when a system is not <see cref="Cadence.Annual"/>, following
    /// <see cref="World.WorldConfig.ConfigHash"/>'s precedent and for its reason. Every system was
    /// annual before cadences were declarable and every system is annual now, so hashing the
    /// default unconditionally would restamp the identity of runs whose histories are byte for byte
    /// what they always were — and this hash travels in the export, where a moved value is supposed
    /// to mean something moved.</para>
    /// </remarks>
    public string SystemOrderHash
    {
        get
        {
            ulong hash = Hash.OfString("systems");
            for (int i = 0; i < _systems.Count; i++)
            {
                hash = Hash.Combine(hash, Hash.OfString(_systems[i].Name));

                Cadence cadence = _systems[i].Cadence;
                if (cadence != Cadence.Annual)
                {
                    // The member's name rather than its number, so that renumbering the enum
                    // cannot silently move every hash, and so a fingerprint that changed can be
                    // grepped for.
                    hash = Hash.Combine(hash, Hash.OfString(cadence.ToString()));
                }
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
        // Day zero, because an annual system has nowhere finer to claim it acted. Dating one to
        // the middle of the year would be inventing a date the model has not earned.
        Stamp now = Stamp.Opening(year);
        world.Now = now;

        // The chronicle is told the same thing the systems are, so an event carries the step it was
        // written in without every recording call having to name one. See IChronicle.OpenStep.
        world.Chronicle.OpenStep(now);

        for (int i = 0; i < _systems.Count; i++)
        {
            world.Chronicle.EnterSystem(i);
            _systems[i].Tick(world, now);
        }

        // Before Observe, so the step's log is settled before anything samples it.
        world.Chronicle.CloseStep();

        Observe(world, year);

        // Leave the clock on the next year to simulate, so Run and Advance can resume.
        world.Now = Stamp.Opening(year + 1);
    }

    /// <summary>
    /// Samples the measures that move, once a year, after every system has run.
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately not a seventeenth system.</b> <see cref="SystemOrder"/> is folded
    /// into the run's identity because every entry in it changes the history that comes out. An
    /// observer that reads state, draws no random numbers and writes nothing back changes none of
    /// it, and declaring it there would make two runs with identical histories claim different
    /// identities — which is the opposite of what that hash is for.</para>
    ///
    /// <para><b>After the systems, so a reading is the year as it ended</b> — the same instant the
    /// export's final-year fields are taken from, which is what makes the last point of every
    /// series agree with the snapshot printed beside it. A realm's effective values are settled at
    /// the top of the year and unchanged since, so reading them here is still reading the values
    /// the year was actually governed by.</para>
    /// </remarks>
    private static void Observe(WorldState world, int year)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            SeriesLog series = world.Series;
            EntityId id = civilization.Id;

            series.Record(id, Measures.Population, year, civilization.Population);

            RealmFortunes fortunes = civilization.Fortunes;
            series.Record(id, Measures.Weariness, year, fortunes.Weariness);
            series.Record(id, Measures.Calamity, year, fortunes.Calamity);
            series.Record(id, Measures.Triumph, year, fortunes.Triumph);
            series.Record(id, Measures.Grievance, year, fortunes.Grievance);

            CultureValues values = civilization.EffectiveValues;
            series.Record(id, Measures.Aggression, year, values.Aggression);
            series.Record(id, Measures.Expansionism, year, values.Expansionism);
            series.Record(id, Measures.Piety, year, values.Piety);
            series.Record(id, Measures.Tradition, year, values.Tradition);
            series.Record(id, Measures.Mercantile, year, values.Mercantile);
            series.Record(id, Measures.Learning, year, values.Learning);
        }

        for (int i = 0; i < world.Settlements.Count; i++)
        {
            Settlement settlement = world.Settlements[i];
            if (!settlement.IsActive) continue;

            world.Series.Record(
                settlement.Id, Measures.Population, year, settlement.Population);
        }

        foreach (TradeRoute route in world.ActiveTradeRoutes())
        {
            world.Series.Record(route.Id, Measures.Traffic, year, route.Traffic);
        }
    }
}

/// <summary>
/// The measures the run samples, and how each one should be read.
/// </summary>
/// <remarks>
/// A measure belongs here when it moves during a run. Anything fixed at worldgen — a culture's
/// values, a person's disposition, a faith's fervour — is already a field in the export, and a
/// flat line drawn across three centuries tells a reader less than the number does.
/// </remarks>
public static class Measures
{
    public static readonly Measure Population = new("population", "", MeasureUnit.Count);

    public static readonly Measure Traffic = new("traffic", "", MeasureUnit.Fraction);

    public static readonly Measure Weariness = new("weariness", "fortunes", MeasureUnit.Fraction);
    public static readonly Measure Calamity = new("calamity", "fortunes", MeasureUnit.Fraction);
    public static readonly Measure Triumph = new("triumph", "fortunes", MeasureUnit.Fraction);
    public static readonly Measure Grievance = new("grievance", "fortunes", MeasureUnit.Fraction);

    public static readonly Measure Aggression = new("aggression", "values", MeasureUnit.Fraction);
    public static readonly Measure Expansionism =
        new("expansionism", "values", MeasureUnit.Fraction);
    public static readonly Measure Piety = new("piety", "values", MeasureUnit.Fraction);
    public static readonly Measure Tradition = new("tradition", "values", MeasureUnit.Fraction);
    public static readonly Measure Mercantile = new("mercantile", "values", MeasureUnit.Fraction);
    public static readonly Measure Learning = new("learning", "values", MeasureUnit.Fraction);
}
