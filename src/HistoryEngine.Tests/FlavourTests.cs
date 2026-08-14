using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Covers the Milestone 8 systems: plague, disaster, faith and the things people make.
/// </summary>
/// <remarks>
/// Two kinds of test here, and the second is the one that matters. <b>Reachability</b> asserts
/// each system fires at all in a standard run — the failure a flavour system is most likely to
/// have is a threshold nobody ever crosses, which looks exactly like a working system that the
/// world simply never triggered. <b>Boundedness</b> asserts it does not take the world over,
/// which is the failure the first cut of the plague system actually had: five pandemics reaching
/// every settlement in the world, eighteen abandonments against M7's one.
/// </remarks>
public sealed class FlavourTests
{
    [Fact]
    public void EveryFlavourSystemFiresInAStandardRun()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        Assert.NotEmpty(Of(run, EventKind.PlagueBegan));
        Assert.NotEmpty(Of(run, EventKind.DisasterStruck));
        Assert.NotEmpty(Of(run, EventKind.ReligionFounded));
        Assert.NotEmpty(Of(run, EventKind.ArtifactCreated));

        // Spread is the part worth having. A faith nobody adopts and a plague that never leaves
        // the town it started in are both indistinguishable from the system not existing.
        Assert.NotEmpty(Of(run, EventKind.ReligionAdopted));
        Assert.NotEmpty(Of(run, EventKind.PlagueSpread));
    }

    /// <summary>
    /// A plague takes a district, not a world.
    /// </summary>
    /// <remarks>
    /// The regression test for the first cut of this system, which had no quarantine term: each
    /// outbreak reached every inhabited settlement in the world, and three centuries produced a
    /// ruin field rather than a history. A third of the world is a catastrophe; all of it is a
    /// modelling error.
    /// </remarks>
    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    [InlineData(2024UL)]
    public void NoPlagueTakesTheWholeWorld(ulong seed)
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(seed));

        int settlements = 0;
        foreach (Settlement settlement in run.World.Settlements)
        {
            if (settlement.IsActive) settlements++;
        }

        foreach (HistoryEvent entry in Of(run, EventKind.PlagueEnded))
        {
            int reached = int.Parse(entry.Data!["reached"]);

            Assert.True(
                reached <= settlements / 2,
                $"An outbreak reached {reached} settlements of {settlements} still standing. " +
                "A plague that takes half the world is the quarantine term having stopped working.");
        }
    }

    /// <summary>
    /// Outbreaks end.
    /// </summary>
    /// <remarks>
    /// A plague that never burns out silently kills a percentage of several settlements every
    /// year for the rest of the run. Nothing in the chronicle would say so — the arrival events
    /// are all in the past — so it would read as an unexplained demographic ceiling.
    /// </remarks>
    [Fact]
    public void PlaguesBurnOut()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        int began = Of(run, EventKind.PlagueBegan).Count;
        int ended = Of(run, EventKind.PlagueEnded).Count;

        // At most the concurrency cap can still be running when the chronicle stops.
        Assert.InRange(began - ended, 0, 2);

        // Nothing still running should have started long ago: an outbreak that survives sixty
        // years is not an epidemic, it is a leak.
        Assert.DoesNotContain(run.World.Outbreaks, o => o.StartYear < run.World.EndYear - 60);
    }

    /// <summary>
    /// Every disaster is one the ground it struck could actually produce.
    /// </summary>
    /// <remarks>
    /// The property that makes this system worth having rather than a random misfortune
    /// generator: a town floods because its region has a river, burns because the region is dry,
    /// and is shaken because the geology there is violent. It means the map explains the
    /// chronicle, and it is exactly the sort of thing that breaks silently when a scoring
    /// threshold is edited.
    /// </remarks>
    [Theory]
    [InlineData(42UL)]
    [InlineData(123UL)]
    public void DisastersMatchTheGroundTheyStruck(ulong seed)
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(seed));

        foreach (HistoryEvent entry in Of(run, EventKind.DisasterStruck))
        {
            Region region = run.World.Regions[entry.Location];
            string kind = entry.Data!["kind"];

            bool possible = kind switch
            {
                "a great flood" => region.HasRiver,
                "a storm off the sea" => region.IsCoastal,
                "an eruption" => region.GeologicActivity >= 0.72 && region.MeanHeight >= 700.0,
                "an earthquake" => region.GeologicActivity >= 0.4,
                "wildfire" => region.Rainfall < 0.55 && region.Temperature > 0.35,
                "a killing winter" => region.Temperature <= 0.3,
                _ => false,
            };

            Assert.True(
                possible,
                $"{region.Id} was struck by {kind}, which its terrain cannot produce " +
                $"(river={region.HasRiver}, coast={region.IsCoastal}, " +
                $"geology={region.GeologicActivity:F2}, height={region.MeanHeight:F0}, " +
                $"rain={region.Rainfall:F2}, temp={region.Temperature:F2}).");
        }
    }

    /// <summary>
    /// A faith and its congregation agree about who follows it.
    /// </summary>
    /// <remarks>
    /// Two structures hold the same fact — the settlement's <see cref="Settlement.ReligionId"/>
    /// and the faith's own list — because both directions are asked for constantly. Conversion,
    /// abandonment and conquest all move a settlement between congregations, and a path that
    /// updates one side and not the other would show up as a faith the viewer says has nine
    /// followers listing eleven settlements.
    /// </remarks>
    [Fact]
    public void FaithsAndTheirCongregationsAgree()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        foreach (Settlement settlement in run.World.Settlements)
        {
            if (!settlement.IsActive || settlement.ReligionId.IsNone) continue;

            Religion faith = run.World.Religions[settlement.ReligionId];

            Assert.Contains(settlement.Id, faith.SettlementIds);
            Assert.True(faith.IsActive, $"{settlement.Id} follows {faith.Id}, which is recorded as forgotten.");
        }

        foreach (Religion faith in run.World.Religions)
        {
            foreach (EntityId settlementId in faith.SettlementIds)
            {
                Assert.Equal(faith.Id, run.World.Settlements[settlementId].ReligionId);
            }

            Assert.True(
                faith.PeakSettlements >= faith.SettlementIds.Count,
                $"{faith.Id} holds more settlements now than its recorded peak.");
        }
    }

    /// <summary>Faiths spread beyond the town that first preached them, and some are forgotten.</summary>
    [Fact]
    public void FaithsSpreadAndCanBeForgotten()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        Assert.Contains(run.World.Religions, faith => faith.PeakSettlements > 1);

        // Ending is reachable, which is what stops the world only ever accumulating faiths. Not
        // asserted on every seed — a run where every faith founded happens to survive is a
        // legitimate history — but the standard world does lose one.
        Assert.Contains(run.World.Religions, faith => !faith.IsActive);
    }

    /// <summary>
    /// An artifact is where its provenance says it is.
    /// </summary>
    /// <remarks>
    /// Provenance is append-only and the holder is a field, so the two can drift — and the paths
    /// that move an artifact are spread across a war, a settlement abandonment and a volcano.
    /// This is the assertion that keeps them honest.
    /// </remarks>
    [Fact]
    public void ArtifactProvenanceMatchesWhereTheyAre()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        Assert.NotEmpty(run.World.Artifacts);

        foreach (Artifact artifact in run.World.Artifacts)
        {
            Assert.NotEmpty(artifact.Provenance);

            int previous = int.MinValue;
            foreach (ArtifactHolding holding in artifact.Provenance)
            {
                Assert.True(holding.Year >= previous, $"{artifact.Id} moved backwards in time.");
                previous = holding.Year;
            }

            ArtifactHolding last = artifact.Provenance[artifact.Provenance.Count - 1];
            Assert.Equal(last.SettlementId, artifact.HolderId);

            if (artifact.IsExtant)
            {
                Assert.False(artifact.HolderId.IsNone);
                Assert.True(
                    run.World.Settlements[artifact.HolderId].IsActive,
                    $"{artifact.Id} is held by a settlement nobody lives in.");
            }
            else
            {
                Assert.True(artifact.HolderId.IsNone);
                Assert.Equal(last.Year, artifact.LostYear);
            }
        }
    }

    /// <summary>
    /// The flavour systems cost no terrain samples.
    /// </summary>
    /// <remarks>
    /// Four systems were added in this milestone and every question they ask about the land —
    /// which regions burn, flood, shake or freeze — is answered from region statistics derived
    /// once at world creation. <c>TerrainDisciplineTests</c> pins the whole run's budget; this
    /// pins the intent, so a later change that reaches for <c>SampleCoarse</c> inside a disaster
    /// roll fails here with a message that says why.
    /// </remarks>
    [Fact]
    public void FlavourSystemsSampleNoTerrain()
    {
        WorldConfig config = TestWorlds.Standard();

        long withFlavour = HistoryRun.Execute(config).SimulationSamples;

        long without = HistoryRun.Execute(
            config,
            inner: null,
            simulator: new Simulator(new IYearSystem[]
            {
                new PopulationSystem(),
                new SettlementLifecycleSystem(),
                new SpecializationSystem(),
                new ExpansionSystem(),
                new DiplomacySystem(),
                new WarSystem(),
                new FigureLifecycleSystem(),
                new SuccessionSystem(),
                new HouseholdSystem(),
            })).SimulationSamples;

        // Not equal: the flavour systems change how many settlements get founded, and founding is
        // what costs samples. What must not happen is the four new systems sampling per entity
        // per year, which would put this into the tens of thousands.
        Assert.True(
            withFlavour <= without + 200,
            $"A run with the flavour systems spent {withFlavour:N0} samples against {without:N0} " +
            "without them. Disasters must read region statistics, never the terrain.");
    }

    private static List<HistoryEvent> Of(HistoryRun run, EventKind kind)
    {
        var found = new List<HistoryEvent>();

        foreach (HistoryEvent entry in run.World.Chronicle.Events)
        {
            if (entry.Kind == kind) found.Add(entry);
        }

        return found;
    }
}
