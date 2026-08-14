using System.Globalization;
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
        Assert.NotEmpty(Of(run, EventKind.ArtifactCopied));

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
    /// Every written artifact contains only people, places and events that existed when it was made.
    /// </summary>
    [Fact]
    public void TomesContainGroundedContemporaryAccounts()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());
        var tomes = new List<Artifact>();

        foreach (Artifact artifact in run.World.Artifacts)
        {
            if (artifact.Kind == ArtifactKind.Tome) tomes.Add(artifact);
        }

        Assert.NotEmpty(tomes);

        int copiesMade = 0;

        foreach (Artifact artifact in tomes)
        {
            TomeContents contents = Assert.IsType<TomeContents>(artifact.TomeContents);
            Assert.NotEmpty(contents.Sections);
            Assert.InRange(contents.CopyLimit, 0, 4);
            Assert.True(contents.Copies.Count <= contents.CopyLimit);
            Assert.False(contents.SubjectId.IsNone);
            Assert.True(ExistedBy(run.World, contents.SubjectId, artifact.CreatedYear));

            EntityKind expectedSubject = contents.Kind switch
            {
                TomeContentKind.Biography or TomeContentKind.Campaign => EntityKind.Figure,
                TomeContentKind.ReligiousRite or TomeContentKind.ReligiousTeaching =>
                    EntityKind.Religion,
                TomeContentKind.ArtifactHistory => EntityKind.Artifact,
                _ => EntityKind.Settlement,
            };

            Assert.Equal(expectedSubject, contents.SubjectId.Kind);

            if (contents.Kind == TomeContentKind.Campaign)
            {
                Assert.Equal(EntityKind.War, contents.ContextId.Kind);

                War war = run.World.Wars[contents.ContextId];
                Assert.Contains(
                    war.BattleIds,
                    id => run.World.Battles[id].Year <= artifact.CreatedYear
                          && (run.World.Battles[id].AttackerCommanderId == contents.SubjectId
                              || run.World.Battles[id].DefenderCommanderId == contents.SubjectId));
            }
            else
            {
                Assert.True(contents.ContextId.IsNone);
            }

            foreach (TomeSection section in contents.Sections)
            {
                Assert.False(string.IsNullOrWhiteSpace(section.Heading));
                Assert.False(string.IsNullOrWhiteSpace(section.Text));

                foreach (EntityId reference in section.References)
                {
                    Assert.True(
                        ExistedBy(run.World, reference, artifact.CreatedYear),
                        $"{artifact.Name} written in {artifact.CreatedYear} cites future or missing {reference}.");
                }
            }

            var destinations = new HashSet<EntityId>();
            int previousCopyYear = artifact.CreatedYear;
            foreach (TomeCopy copy in contents.Copies)
            {
                copiesMade++;
                Assert.True(copy.Year > artifact.CreatedYear);
                Assert.True(copy.Year >= previousCopyYear);
                Assert.True(destinations.Add(copy.SettlementId));
                Assert.NotEqual(copy.SourceSettlementId, copy.SettlementId);
                Assert.True(SettlementWasActive(run.World, copy.SourceSettlementId, copy.Year));
                Assert.True(SettlementWasActive(run.World, copy.SettlementId, copy.Year));

                bool sourceHadExemplar = OriginalWasHeldAt(
                    artifact, copy.SourceSettlementId, copy.Year);
                foreach (TomeCopy earlier in contents.Copies)
                {
                    if (earlier.Year >= copy.Year) break;
                    if (earlier.SettlementId == copy.SourceSettlementId) sourceHadExemplar = true;
                }

                Assert.True(
                    sourceHadExemplar,
                    $"{artifact.Name} was copied from {copy.SourceSettlementId} in {copy.Year}, " +
                    "but no exemplar had reached that settlement.");
                Assert.Contains(
                    run.World.Chronicle.Events,
                    entry => entry.Kind == EventKind.ArtifactCopied
                        && entry.Year == copy.Year
                        && entry.Subject == artifact.Id
                        && entry.Object == copy.SourceSettlementId
                        && entry.Location == copy.SettlementId);

                previousCopyYear = copy.Year;
            }
        }

        Assert.Equal(copiesMade, Of(run, EventKind.ArtifactCopied).Count);
    }

    /// <summary>
    /// Copying is common enough to form a network, but bounded enough that manuscripts stay scarce.
    /// </summary>
    [Fact]
    public void TomeCirculationIsCommonButBounded()
    {
        int tomes = 0;
        int eligible = 0;
        int distributed = 0;
        int copies = 0;

        for (ulong seed = 1; seed <= 8; seed++)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Artifact artifact in world.Artifacts)
            {
                if (artifact.TomeContents is not TomeContents contents) continue;

                tomes++;
                if (contents.CopyLimit > 0) eligible++;
                if (contents.Copies.Count > 0) distributed++;
                copies += contents.Copies.Count;
                Assert.InRange(contents.Copies.Count, 0, 4);
            }
        }

        Assert.True(tomes >= 12, $"Only {tomes} tomes were made; the sample cannot calibrate circulation.");
        Assert.InRange((double)eligible / tomes, 0.60, 0.90);
        Assert.InRange((double)distributed / tomes, 0.50, 0.85);
        Assert.InRange((double)copies / tomes, 0.75, 2.00);
    }

    /// <summary>
    /// An artifact history explains the object's purpose and freezes its whereabouts at writing.
    /// </summary>
    [Fact]
    public void ArtifactHistoryTomesDescribeMakingAndLastRecord()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;
        Settlement authoring = world.Settlements.First(
            settlement => settlement.IsActive && settlement.Tier >= SettlementTier.Town);
        Settlement destination = world.Settlements.First(
            settlement => settlement.IsActive && settlement.Id != authoring.Id);
        Civilization civilization = world.Civilizations[authoring.CivilizationId];

        int made = Math.Min(world.EndYear - 2, Math.Max(authoring.FoundedYear, world.EndYear - 20));

        var extant = new Artifact(
            world.Artifacts.NextId,
            "the Blade of the Test Chronicle",
            ArtifactKind.Weapon,
            authoring.Id,
            made);
        extant.MoveTo(destination.Id, world.EndYear - 1, "given in tribute");
        world.Artifacts.Add(extant);

        var lost = new Artifact(
            world.Artifacts.NextId,
            "the Crown of the Test Chronicle",
            ArtifactKind.Regalia,
            authoring.Id,
            made);
        lost.MoveTo(destination.Id, world.EndYear - 5, "carried for safekeeping");
        lost.Lose(world.EndYear - 1, "in a fire");
        world.Artifacts.Add(lost);

        TomeContents? extantAccount = null;
        TomeContents? lostAccount = null;
        TomeContents? beforeLossAccount = null;

        for (int i = 0; i < 4096 && (extantAccount is null || lostAccount is null); i++)
        {
            TomeContents candidate = Tomes.Compose(
                world,
                authoring,
                civilization,
                new EntityId(EntityKind.Artifact, 10_000 + i),
                world.EndYear);

            if (candidate.Kind != TomeContentKind.ArtifactHistory) continue;
            if (candidate.SubjectId == extant.Id) extantAccount = candidate;
            if (candidate.SubjectId == lost.Id) lostAccount = candidate;
        }

        for (int i = 0; i < 4096 && beforeLossAccount is null; i++)
        {
            TomeContents candidate = Tomes.Compose(
                world,
                authoring,
                civilization,
                new EntityId(EntityKind.Artifact, 20_000 + i),
                world.EndYear - 2);

            if (candidate.Kind == TomeContentKind.ArtifactHistory
                && candidate.SubjectId == lost.Id)
            {
                beforeLossAccount = candidate;
            }
        }

        TomeContents extantHistory = Assert.IsType<TomeContents>(extantAccount);
        TomeContents lostHistory = Assert.IsType<TomeContents>(lostAccount);
        TomeContents beforeLossHistory = Assert.IsType<TomeContents>(beforeLossAccount);

        TomeSection making = Assert.Single(
            extantHistory.Sections, section => section.Heading == "Making");
        Assert.Contains(made.ToString(CultureInfo.InvariantCulture), making.Text);
        Assert.Contains(authoring.Name, making.Text);
        Assert.Contains("war and defence", making.Text);

        TomeSection extantLast = Assert.Single(
            extantHistory.Sections, section => section.Heading == "Last record");
        Assert.Contains(destination.Name, extantLast.Text);
        Assert.Contains("last recorded", extantLast.Text);

        TomeSection lostLast = Assert.Single(
            lostHistory.Sections, section => section.Heading == "Last record");
        Assert.Contains("had been lost", lostLast.Text);
        Assert.Contains(destination.Name, lostLast.Text);
        Assert.Contains((world.EndYear - 1).ToString(CultureInfo.InvariantCulture), lostLast.Text);
        Assert.Contains("in a fire", lostLast.Text);

        TomeSection beforeLossLast = Assert.Single(
            beforeLossHistory.Sections, section => section.Heading == "Last record");
        Assert.Contains(destination.Name, beforeLossLast.Text);
        Assert.DoesNotContain("lost", beforeLossLast.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The focused campaign form is reachable and records actual command.</summary>
    [Fact]
    public void CampaignTomesCanDescribeAFiguresServiceInWar()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;
        TomeContents? account = null;

        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive || settlement.Tier < SettlementTier.Town) continue;

            Civilization civilization = world.Civilizations[settlement.CivilizationId];
            for (int i = 0; i < 128; i++)
            {
                TomeContents candidate = Tomes.Compose(
                    world,
                    settlement,
                    civilization,
                    new EntityId(EntityKind.Artifact, i),
                    world.EndYear);

                if (candidate.Kind != TomeContentKind.Campaign) continue;
                account = candidate;
                break;
            }

            if (account is not null) break;
        }

        TomeContents campaign = Assert.IsType<TomeContents>(account);
        War war = world.Wars[campaign.ContextId];

        Assert.Contains(
            war.BattleIds,
            id => world.Battles[id].AttackerCommanderId == campaign.SubjectId
                  || world.Battles[id].DefenderCommanderId == campaign.SubjectId);
        Assert.Contains(campaign.Sections, section => section.Heading == "Recorded engagements");
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

    private static bool OriginalWasHeldAt(Artifact artifact, EntityId settlementId, int year)
    {
        EntityId holder = EntityId.None;
        foreach (ArtifactHolding holding in artifact.Provenance)
        {
            if (holding.Year > year) break;
            holder = holding.SettlementId;
        }

        return holder == settlementId;
    }

    private static bool SettlementWasActive(WorldState world, EntityId id, int year)
    {
        if (!world.Settlements.Contains(id)) return false;

        Settlement settlement = world.Settlements[id];
        return settlement.FoundedYear <= year
            && (settlement.AbandonedYear is null || settlement.AbandonedYear > year);
    }

    private static bool ExistedBy(WorldState world, EntityId id, int year) => id.Kind switch
    {
        EntityKind.Culture => world.Cultures.Contains(id),
        EntityKind.Civilization => world.Civilizations.Contains(id)
            && world.Civilizations[id].FoundedYear <= year,
        EntityKind.Settlement => world.Settlements.Contains(id)
            && world.Settlements[id].FoundedYear <= year,
        EntityKind.Figure => world.Figures.Contains(id) && world.Figures[id].BirthYear <= year,
        EntityKind.Dynasty => world.Dynasties.Contains(id) && world.Dynasties[id].FoundedYear <= year,
        EntityKind.War => world.Wars.Contains(id) && world.Wars[id].StartYear <= year,
        EntityKind.Battle => world.Battles.Contains(id) && world.Battles[id].Year <= year,
        EntityKind.Region => world.Regions.Contains(id),
        EntityKind.Artifact => world.Artifacts.Contains(id)
            && world.Artifacts[id].CreatedYear <= year,
        EntityKind.Religion => world.Religions.Contains(id)
            && world.Religions[id].FoundedYear <= year,
        EntityKind.TradeRoute => world.TradeRoutes.Contains(id)
            && world.TradeRoutes[id].FoundedYear <= year,
        _ => false,
    };
}
