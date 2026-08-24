using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Serialization;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// What the rolled sky does, and what the people standing under it wrote down.
/// </summary>
public sealed class SkywatchTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public SkywatchTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Every apparition is a real return of a real comet, on the period the seed rolled.
    /// </summary>
    /// <remarks>
    /// The assertion the whole milestone rests on. If an apparition can happen in a year the orbit
    /// does not produce, then a later prediction is being checked against a fiction and there is no
    /// difference between knowing something and being told it.
    /// </remarks>
    [Fact]
    public void EveryApparitionIsOnItsCometsRolledPeriod()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = WorldBuilder.Create(TestWorlds.Standard(seed));
            WorldCosmology sky = world.Flavour.Cosmology;
            List<Apparition> returns = Skywatch.Apparitions(world);

            var byComet = new Dictionary<int, List<int>>();
            foreach (Apparition seen in returns)
            {
                Assert.InRange(seen.Year, world.StartYear, world.EndYear);
                Assert.Contains(sky.Comets, comet => comet.Index == seen.CometIndex);

                if (!byComet.TryGetValue(seen.CometIndex, out List<int>? years))
                {
                    years = new List<int>();
                    byComet[seen.CometIndex] = years;
                }

                years.Add(seen.Year);
            }

            foreach ((int index, List<int> years) in byComet)
            {
                SystemComet comet = sky.Comets.Single(item => item.Index == index);
                double period = Skywatch.PeriodYears(sky, comet);

                years.Sort();
                for (int i = 1; i < years.Count; i++)
                {
                    // Rounded to whole years, so successive returns sit within a year of the period.
                    Assert.InRange(years[i] - years[i - 1], period - 1.0, period + 1.0);
                }
            }
        }
    }

    /// <summary>
    /// A faint comet has to be rare, and a bright one does not.
    /// </summary>
    /// <remarks>
    /// Seed 7 rolls a faint comet on a ten-year period. Under a brightness-only rule it returned
    /// thirty times in three centuries and was written down a hundred and forty-eight times, which
    /// is a chronicle of the weather. This is the rule that keeps it out, stated in the terms it was
    /// written in rather than by asserting the count on one seed.
    /// </remarks>
    [Fact]
    public void AFaintCometIsChronicledOnlyWhenItIsRare()
    {
        foreach (ulong seed in Seeds)
        {
            WorldCosmology sky = WorldBuilder.Create(TestWorlds.Standard(seed)).Flavour.Cosmology;

            foreach (SystemComet comet in sky.Comets)
            {
                if (!Skywatch.Chronicled(sky, comet, out ApparitionGrade grade, out double period))
                {
                    continue;
                }

                Assert.True(
                    grade > ApparitionGrade.Faint || period >= 25.0,
                    $"Seed {seed}: comet {comet.Index} is faint and returns every {period:F1} "
                    + "years, and is still being written down.");
            }
        }
    }

    /// <summary>
    /// The sky is the same sky whatever the history does under it.
    /// </summary>
    /// <remarks>
    /// Apparitions are derived from the roll and never stored, so a world simulated for three
    /// centuries and one never simulated at all must agree about every return. This is what makes
    /// the schedule usable as the thing a prediction is checked against.
    /// </remarks>
    [Fact]
    public void TheScheduleDoesNotDependOnTheHistory()
    {
        foreach (ulong seed in Seeds)
        {
            List<Apparition> unrun = Skywatch.Apparitions(WorldBuilder.Create(TestWorlds.Standard(seed)));
            List<Apparition> lived = Skywatch.Apparitions(HistoryRun.Execute(TestWorlds.Standard(seed)).World);

            Assert.Equal(unrun, lived);
        }
    }

    /// <summary>
    /// Nobody records a comet in a year it did not come back.
    /// </summary>
    [Fact]
    public void EveryObservationSitsOnAnApparition()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            var schedule = new HashSet<(int, int)>(
                Skywatch.Apparitions(world).Select(seen => (seen.CometIndex, seen.Year)));

            foreach (Figure figure in world.Figures)
            {
                foreach (SkyObservation seen in figure.Observations)
                {
                    Assert.Contains((seen.CometIndex, seen.Year), schedule);

                    // They were alive, grown, and somewhere, when they wrote it down.
                    Assert.True(figure.BirthYear + Succession.MajorityAge <= seen.Year);
                    Assert.True((figure.DeathYear ?? world.EndYear) >= seen.Year);
                    Assert.True(world.Settlements.Contains(seen.SettlementId));

                    if (seen.PriorYear is int prior)
                    {
                        Assert.True(prior < seen.Year);
                        Assert.Equal(seen.Year - prior, seen.Interval);
                        Assert.Contains((seen.CometIndex, prior), schedule);
                    }
                    else
                    {
                        Assert.Null(seen.Interval);
                    }
                }
            }
        }
    }

    /// <summary>
    /// An interval is only ever what the observer's own realm had on record.
    /// </summary>
    /// <remarks>
    /// The rule that keeps a later claim honest. A scribe cannot count from a sighting nobody near
    /// them wrote down, so a realm that has never recorded this body before produces an observation
    /// with no interval — however many times the comet has actually been round.
    /// </remarks>
    [Fact]
    public void AnIntervalIsOnlyWhatTheObserversOwnRealmHadOnRecord()
    {
        int checkedIntervals = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (SkyObservation seen in figure.Observations)
                {
                    if (seen.PriorYear is not int prior) continue;

                    bool ownRealmHadIt = world.Figures.Any(other =>
                        other.Observations.Any(earlier =>
                            earlier.RealmId == seen.RealmId
                            && earlier.CometIndex == seen.CometIndex
                            && earlier.Year == prior));

                    Assert.True(
                        ownRealmHadIt,
                        $"Seed {seed}: {figure.FullName} counted from {prior}, which their realm "
                        + "never recorded.");
                    checkedIntervals++;
                }
            }
        }

        Assert.True(checkedIntervals > 0, "No realm across the panel ever saw the same comet twice.");
    }

    /// <summary>
    /// Looking up is neither universal nor impossible, and the sky differs by world.
    /// </summary>
    /// <remarks>
    /// The panel deliberately contains a world with a dull sky. Some seeds roll nothing worth
    /// writing down for three hundred years, and a model that guaranteed every world an astronomy
    /// would be asserting something about the generator that is not true.
    /// </remarks>
    [Fact]
    public void RecordingIsNeitherUniversalNorImpossibleAcrossThePanel()
    {
        int worldsWithASky = 0;
        int totalApparitions = 0;
        int totalRecords = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            List<Apparition> schedule = Skywatch.Apparitions(world);
            var observations = world.Figures.SelectMany(figure => figure.Observations).ToList();
            int realms = world.Civilizations.Count();

            _output.WriteLine(
                $"seed {seed} [{world.Flavour.Kind} {world.Flavour.Name}]: "
                + $"comets={world.Flavour.Cosmology.Comets.Count} chronicled-returns={schedule.Count} "
                + $"records={observations.Count} watchers="
                + $"{world.Figures.Count(figure => figure.Observations.Count > 0)} "
                + $"intervals={observations.Count(seen => seen.PriorYear is not null)}");

            totalApparitions += schedule.Count;
            totalRecords += observations.Count;
            if (schedule.Count > 0) worldsWithASky++;

            // No apparition may be recorded twice by the same realm, and never by more realms
            // than exist.
            foreach (Apparition seen in schedule)
            {
                // One register per realm per return. Grouped by the realm recorded at the time,
                // because the writer may have changed realms in the two centuries since.
                var realmsThatSawIt = new List<EntityId>();
                foreach (Figure figure in world.Figures)
                {
                    foreach (SkyObservation wrote in figure.Observations)
                    {
                        if (wrote.CometIndex != seen.CometIndex || wrote.Year != seen.Year) continue;

                        Assert.DoesNotContain(wrote.RealmId, realmsThatSawIt);
                        realmsThatSawIt.Add(wrote.RealmId);
                    }
                }

                Assert.True(realmsThatSawIt.Count <= realms);
            }
        }

        Assert.True(worldsWithASky >= 3, "Too few worlds in the panel have anything in the sky.");
        Assert.True(totalRecords > 0, "Nobody in any world ever wrote a comet down.");
        Assert.True(
            totalRecords < totalApparitions * 8,
            "Every realm recorded every return, which is a world of astronomers.");
    }

    /// <summary>
    /// Whether one realm looked up cannot depend on another realm existing.
    /// </summary>
    /// <remarks>
    /// The fork is the comet, the year and the realm, so a realm founded on the far side of the map
    /// cannot change what a scribe here wrote down. Run the same apparition against the same realm
    /// twice, once with a crowd of unrelated people in the world, and the answer must not move.
    /// </remarks>
    [Fact]
    public void AnUnrelatedRealmCannotChangeWhoLookedUp()
    {
        Assert.Equal(Watched(bystanders: 0), Watched(bystanders: 60));

        static List<string> Watched(int bystanders)
        {
            WorldState world = WorldBuilder.Create(TestWorlds.Standard(11));
            Civilization civilization = world.Civilizations[EntityId.Civilization(0)];

            var scribe = new Figure(
                EntityId.Figure(5000),
                civilization.Id,
                civilization.CultureId,
                "Aldis",
                Sex.Female,
                1)
            {
                Occupation = Occupation.Scribe,
                ResidenceSettlementId = civilization.CapitalId,
            };
            world.Figures.Add(scribe);

            for (int i = 0; i < bystanders; i++)
            {
                world.Figures.Add(new Figure(
                    EntityId.Figure(6000 + i),
                    civilization.Id,
                    civilization.CultureId,
                    "Bystander" + i,
                    Sex.Male,
                    1)
                {
                    Occupation = Occupation.Townsfolk,
                    ResidenceSettlementId = civilization.CapitalId,
                });
            }

            foreach (Apparition seen in Skywatch.Apparitions(world))
            {
                Skywatch.Record(world, seen.Year);
            }

            var wrote = new List<string>();
            foreach (SkyObservation seen in scribe.Observations)
            {
                wrote.Add($"{seen.CometIndex}:{seen.Year}:{seen.Grade}:{seen.PriorYear}");
            }

            return wrote;
        }
    }

    /// <summary>
    /// A derived interval is always the true period or a whole multiple of it.
    /// </summary>
    /// <remarks>
    /// <para>The most valuable thing this model produces, and it was not designed in. A realm that
    /// missed a return — no scribe that decade, or a war on — counts from the one before, and gets a
    /// clean multiple of the truth. Seed 11 produces both readings of the same comet: six people
    /// derive 74 or 75 years for a body whose period is 74.8, and two derive 149 or 150.</para>
    ///
    /// <para>That is a real error mode of real astronomy arriving for free, and it is the thing that
    /// makes a prediction worth adjudicating: someone with a century and a half of honest evidence
    /// will name the wrong year, and the sky will say so. If this ever stops holding — if an
    /// interval appears that is not a multiple — then the register and the schedule have come
    /// apart and no claim built on either can be trusted.</para>
    /// </remarks>
    [Fact]
    public void ADerivedIntervalIsTheTruePeriodOrAWholeMultipleOfIt()
    {
        int exact = 0;
        int multiples = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            WorldCosmology sky = world.Flavour.Cosmology;

            foreach (Figure figure in world.Figures)
            {
                foreach (SkyObservation seen in figure.Observations)
                {
                    if (seen.Interval is not int interval) continue;

                    SystemComet comet = sky.Comets.Single(item => item.Index == seen.CometIndex);
                    double period = Skywatch.PeriodYears(sky, comet);
                    double turns = interval / period;
                    double nearest = Math.Round(turns);

                    Assert.True(nearest >= 1.0);
                    Assert.True(
                        Math.Abs(turns - nearest) < 0.05,
                        $"Seed {seed}: {figure.FullName} derived {interval} years for a comet on "
                        + $"{period:F1}, which is {turns:F2} returns and therefore neither the "
                        + "period nor a count of missed ones.");

                    if (nearest == 1.0) exact++;
                    else multiples++;
                }
            }
        }

        _output.WriteLine($"intervals derived: {exact} exact, {multiples} off by a missed return");
        Assert.True(exact > 0, "Nobody across the panel ever derived a comet's actual period.");
        Assert.True(
            multiples > 0,
            "No realm across the panel ever missed a return and counted double, which is the "
            + "mistake that makes a prediction worth checking.");
    }

    /// <summary>
    /// A comet is the one formative thing that can happen to somebody nothing happens to.
    /// </summary>
    [Fact]
    public void SeeingSomethingLeavesAMemoryAndReachesTheExport()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(11));
        WorldState world = run.World;
        WorldExport export = run.ToExport();

        var watchers = world.Figures.Where(figure => figure.Observations.Count > 0).ToList();
        Assert.NotEmpty(watchers);

        foreach (Figure figure in watchers)
        {
            ExportFigure exported = export.Figures.Single(item => item.Id == figure.Id);
            Assert.Equal(figure.Observations.Count, exported.Observations.Count);
            Assert.All(exported.Observations, seen => Assert.NotNull(seen.SettlementId));
            Assert.All(exported.Observations, seen => Assert.NotNull(seen.RealmId));
        }

        // Most keep it, not all: a memory of the sky competes for the same twelve slots as
        // everything that happened to them, and a life full of bereavements can crowd it out.
        // That is the memory model working, not this one failing.
        int keptIt = watchers.Count(figure =>
            figure.Memories.Any(memory => memory.Kind == MemoryKind.Wonder));
        Assert.True(
            keptIt > watchers.Count / 2,
            $"Only {keptIt} of {watchers.Count} watchers still carry what they saw.");

        // The sky's own schedule travels beside the register, which is what makes the register
        // checkable rather than merely readable.
        Assert.NotEmpty(export.World.Cosmology.Apparitions);
        Assert.All(
            export.World.Cosmology.Apparitions,
            seen => Assert.InRange(seen.Year, export.Meta.StartYear, export.Meta.EndYear));

        var schedule = new HashSet<(int, int)>(
            export.World.Cosmology.Apparitions.Select(seen => (seen.CometIndex, seen.Year)));
        foreach (ExportFigure figure in export.Figures)
        {
            foreach (ExportObservation seen in figure.Observations)
            {
                Assert.Contains((seen.CometIndex, seen.Year), schedule);
            }
        }
    }
}
