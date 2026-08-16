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
/// <para><b>The year is stepped in seasons, and most systems run in the first of them.</b> An
/// <see cref="Cadence.Annual"/> system ticks in the opening season and not the others, which is
/// exactly what it did when the year was one step: it sees the world in the order it always saw it
/// and cannot tell that three more steps follow. That is what lets systems be re-phased one at a
/// time, each with its own readable calibration, instead of in one change that moves everything.</para>
///
/// <para>An <see cref="Cadence.Episodic"/> system is never reached by this loop at all — it is
/// woken by the docket, so a system with nothing scheduled costs nothing. Nothing here iterates
/// days, and nothing ever should.</para>
/// </remarks>
public sealed class Simulator
{
    private readonly IReadOnlyList<ISystem> _systems;

    /// <summary>
    /// Who answers for each kind of scheduled work, indexed by the kind itself.
    /// </summary>
    /// <remarks>
    /// An array rather than a dictionary, and not only to satisfy the determinism guard that
    /// rejects one. <see cref="DocketKind"/> is a small dense enum whose values are already fixed
    /// and numbered because they are part of the docket's ordering key — so the kind <em>is</em> an
    /// index, and a hash lookup would be a slower way of reaching the same slot through a structure
    /// with an iteration order nobody should ever be tempted to walk.
    /// </remarks>
    private readonly (IEpisodic? Handler, int Index)[] _episodic;

    public Simulator(IReadOnlyList<ISystem>? systems = null)
    {
        _systems = systems ?? DefaultSystems();

        int kinds = 0;
        foreach (DocketKind kind in Enum.GetValues<DocketKind>())
        {
            kinds = Math.Max(kinds, (int)kind + 1);
        }

        _episodic = new (IEpisodic?, int)[kinds];

        for (int i = 0; i < _systems.Count; i++)
        {
            ISystem system = _systems[i];

            if (system is IEpisodic episodic)
            {
                if (episodic.Handles.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"System '{system.Name}' answers for scheduled work but names no kind.");
                }

                foreach (DocketKind kind in episodic.Handles)
                {
                    if (kind == DocketKind.None)
                    {
                        throw new InvalidOperationException(
                            $"System '{system.Name}' claims DocketKind.None, which is the absence "
                            + "of an entry and never a claim on one.");
                    }

                    if (_episodic[(int)kind].Handler is IEpisodic held)
                    {
                        throw new InvalidOperationException(
                            $"Systems '{((ISystem)held).Name}' and '{system.Name}' both answer for "
                            + $"{kind}. One kind has one owner, or which of them resolves an entry "
                            + "depends on the order they were listed in.");
                    }

                    _episodic[(int)kind] = (episodic, i);
                }
            }
            else if (system.Cadence == Cadence.Episodic)
            {
                // Nothing would ever run it: the clock skips an episodic system by definition, and
                // without a declared kind the docket cannot wake it either.
                throw new InvalidOperationException(
                    $"System '{system.Name}' is Episodic but implements no IEpisodic, so nothing "
                    + "would ever run it.");
            }
        }
    }

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
    /// <para><b>Those springs now exist.</b> Several systems are described above as if the year had
    /// parts — "a war declared this spring is fought this summer", "a province taken in a spring
    /// campaign" — and they were written down before there was a calendar to say it with.
    /// <c>war</c> and <c>expansion</c> have their seasons, landed one at a time because each
    /// re-phasing changes every history in the world and moving several at once would present one
    /// fingerprint change with several calibrations inside it.</para>
    ///
    /// <para><b><c>crown</c> was listed with them and should not have been.</b> It fades a realm's
    /// memory by a year and settles the values that memory produces — a slow accumulator, which is
    /// the case the three-tier argument explicitly reserves for the year: dividing a yearly fade
    /// into four gives the same answer through four times the arithmetic and buys no decision that
    /// a season could inform. "A crown made in the reign of a ruler crowned this spring" is a claim
    /// about <c>artifacts</c> running after the houses, and it is already true of the ordering
    /// without <c>crown</c> itself having a season. It stays annual on purpose.</para>
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

    /// <summary>
    /// Whether a system of this cadence runs in this season of the year.
    /// </summary>
    /// <remarks>
    /// <para><b>An annual system runs in the opening season and not the others</b>, which is what
    /// makes the whole re-phasing safe to land one system at a time: an annual system sees the
    /// world in the order it always saw it, at the step it always ran in, and cannot tell that
    /// three more steps happen after it.</para>
    ///
    /// <para>An episodic system is never run by the clock at all. It is woken by the docket, and a
    /// system with nothing scheduled costs nothing — the property that makes the day affordable
    /// without ever being iterated.</para>
    /// </remarks>
    private static bool RunsIn(Cadence cadence, int season) => cadence switch
    {
        Cadence.Annual => season == 0,
        Cadence.Seasonal => true,
        _ => false,
    };

    /// <summary>
    /// Hands every entry that has fallen due to whoever answers for its kind.
    /// </summary>
    /// <remarks>
    /// <para>Drained in the docket's own order, which is a total order over
    /// <c>(absolute day, kind, subject, sequence)</c> — so what resolves first is a property of what
    /// was scheduled and never of which system asked. That is the whole reason this loop is here
    /// rather than in the systems.</para>
    ///
    /// <para>An entry whose kind nobody owns throws rather than being dropped. A silently discarded
    /// entry is a siege that never lifts or a party that never arrives, and it would present as a
    /// war that would not end — a long way from the line that caused it.</para>
    ///
    /// <para>Each entry is stamped at its own due day and attributed to its handler's place in the
    /// system order, so the step's sort puts it where it belongs among that step's own writing.</para>
    /// </remarks>
    private void ResolveDue(WorldState world, Stamp now)
    {
        while (world.Docket.TryTakeDue(now, out DocketEntry entry))
        {
            (IEpisodic? handler, int index) = _episodic[(int)entry.Kind];

            if (handler is null)
            {
                throw new InvalidOperationException(
                    $"Nothing answers for {entry.Kind}, but an entry of it came due at {entry.Due}.");
            }

            world.Chronicle.EnterSystem(index);
            world.Chronicle.StampAt(entry.Due);

            handler.Resolve(world, entry, now);
        }

        // Back to the step's own day, so a system that writes after this is dated by the clock
        // rather than by whatever happened to be last off the queue.
        world.Chronicle.StampAt(now);
    }

    private void Tick(WorldState world, int year)
    {
        Calendar calendar = world.Config.Calendar;

        for (int season = 0; season < calendar.SeasonsPerYear; season++)
        {
            // The step's day is the season's first, because a system ticking a season acts across
            // it rather than on a particular morning of it. A system that earns a finer date says
            // so by scheduling on the docket, which is the only thing in the engine that names a
            // day the clock did not.
            Stamp now = new Stamp(year, season * calendar.DaysPerSeason);
            world.Now = now;

            // The chronicle is told the same thing the systems are, so an event carries the step it
            // was written in without every recording call having to name one. See OpenStep.
            world.Chronicle.OpenStep(now);

            // Work that fell due since the last step is resolved before this one's systems run.
            // It already happened — the docket only hands back what is owed at or before now — so
            // the systems should see a world it has been applied to rather than one it is about to
            // be.
            ResolveDue(world, now);

            for (int i = 0; i < _systems.Count; i++)
            {
                if (!RunsIn(_systems[i].Cadence, season)) continue;

                world.Chronicle.EnterSystem(i);
                _systems[i].Tick(world, now);
            }

            // Before the next step opens, so the log is settled while it is still this step's.
            world.Chronicle.CloseStep();
        }

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
