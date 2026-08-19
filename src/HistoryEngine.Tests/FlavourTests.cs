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
    /// <summary>Seeds sampled where the question needs more history than one world provides.</summary>
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    [Fact]
    public void EveryFlavourSystemFiresInAStandardRun()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        Assert.NotEmpty(Of(run, EventKind.PlagueBegan));
        Assert.NotEmpty(Of(run, EventKind.DisasterStruck));
        Assert.NotEmpty(Of(run, EventKind.ReligionFounded));
        Assert.NotEmpty(Of(run, EventKind.ArtifactCreated));

        // Copying is sampled across seeds rather than asserted on this one, which is what the
        // Seeds array above exists for. A world produces nine to fourteen artifacts and none to six
        // copies of them, so whether one seed copies anything is luck rather than evidence: seed 42
        // draws a zero the moment any change moves its history, while 2, 7, 11 and 99 return 2, 5,
        // 2 and 6. Asserted on one world it measures the seed; asserted on five it measures the
        // system, which is what the name of this test claims.
        int copied = 0;
        foreach (ulong seed in Seeds)
        {
            copied += Of(HistoryRun.Execute(TestWorlds.Standard(seed)), EventKind.ArtifactCopied).Count;
        }

        Assert.True(copied > 0, "No tome was copied in any sampled world.");

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

    /// <summary>
    /// A figure's faith is their own, and when they have one it is a real church.
    /// </summary>
    /// <remarks>
    /// Personal rather than a lookup of the town they live in. The assertion is the weaker of
    /// the two interesting properties — that the field is populated and resolvable — because
    /// divergence from the residence is real but not guaranteed in every seed. The stronger
    /// one, that the faith colours their disposition, lives in <c>DispositionTests</c>.
    /// </remarks>
    [Fact]
    public void FiguresFollowARealFaith()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        int faithful = 0;

        foreach (Figure figure in world.Figures)
        {
            if (figure.ReligionId.IsNone) continue;

            faithful++;
            Assert.True(
                world.Religions.Contains(figure.ReligionId),
                $"{figure.Id} follows {figure.ReligionId}, which is not a faith in this world.");
        }

        Assert.True(faithful > 0, "No figure in a standard world followed any faith.");
    }

    /// <summary>Faiths spread beyond the town that first preached them, and some are forgotten.</summary>
    [Fact]
    public void FaithsSpreadAndCanBeForgotten()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        Assert.Contains(run.World.Religions, faith => faith.PeakSettlements > 1);

        // Ending is reachable, which is what stops the world only ever accumulating faiths. It is
        // deliberately checked on a known fading seed rather than requiring every standard world
        // to lose a faith: a history where every faith happens to survive is legitimate.
        //
        // Seed 8 rather than seed 11, which used to lose exactly one faith and stopped when the
        // reign-aware layer perturbed it. A seed chosen for having a single qualifying event is a
        // seed that will go stale on the next calibration change; this one loses nine of sixteen,
        // so it is testing that the mechanism works rather than that one history is unchanged.
        HistoryRun fadingRun = HistoryRun.Execute(TestWorlds.Standard(seed: 8));
        bool forgotten = AnyFaithEnded(fadingRun.World);
        if (!forgotten)
        {
            foreach (ulong seed in new ulong[] { 3, 5, 7, 11, 13, 17, 19, 23, 42, 99 })
            {
                fadingRun = HistoryRun.Execute(TestWorlds.Standard(seed));
                if (AnyFaithEnded(fadingRun.World))
                {
                    forgotten = true;
                    break;
                }
            }
        }

        Assert.True(forgotten, "No faith was ever forgotten across the sampled seeds.");
    }

    /// <summary>
    /// A faith is more than a name and a fervour. The rest of its character is rolled at
    /// founding, stays inside [0, 1], and actually varies across a world.
    /// </summary>
    [Fact]
    public void FaithsCarryACharacterThatVaries()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());

        Assert.NotEmpty(run.World.Religions);

        var deities = new HashSet<DeityStructure>();
        var authorities = new HashSet<AuthorityType>();

        foreach (Religion faith in run.World.Religions)
        {
            FaithCharacter character = faith.Character;

            Assert.Equal(character.Fervour, faith.Fervour);
            Assert.InRange(character.Fervour, 0.0, 1.0);
            Assert.InRange(character.Zealotry, 0.0, 1.0);
            Assert.InRange(character.Tolerance, 0.0, 1.0);
            Assert.InRange(character.SchismProneness, 0.0, 1.0);
            Assert.InRange(character.Syncretism, 0.0, 1.0);

            deities.Add(character.Deity);
            authorities.Add(character.Authority);

            if (!faith.ParentId.IsNone)
            {
                Assert.True(run.World.Religions.Contains(faith.ParentId));
            }
        }

        Assert.True(
            deities.Count > 1,
            "A standard world should preach more than one kind of god.");
        Assert.True(
            authorities.Count > 1,
            "A standard world should raise more than one kind of church.");
    }

    /// <summary>
    /// A faith that admits only one sex to holy office does not invent a high priest of the other.
    /// </summary>
    [Fact]
    public void InventedHighPriestsHonourClergyAdmission()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());
        int checkedClerics = 0;

        foreach (Figure figure in run.World.Figures)
        {
            if (figure.Origin != FigureOrigin.Clergy) continue;

            OfficeHolding? held = figure.OpenOffice(OfficeKind.HighPriest)
                                  ?? figure.Offices.Find(o => o.Kind == OfficeKind.HighPriest);
            if (held is null || held.ScopeId.IsNone || !run.World.Religions.Contains(held.ScopeId))
            {
                continue;
            }

            FaithCharacter character = run.World.Religions[held.ScopeId].Character;
            Assert.True(
                character.Admits(figure.Sex),
                $"{figure.FullName} is {figure.Sex} clergy of a faith that admits {character.Clergy}.");
            checkedClerics++;
        }

        Assert.True(checkedClerics > 0, "A standard world should raise clergy into the record.");
    }

    /// <summary>
    /// Faith leaves places on the map: ordinary houses of worship inside settlements and rarer
    /// sanctuaries with coordinates of their own.
    /// </summary>
    [Fact]
    public void HolySitesCanStandWithinSettlementsOrOnTheirOwn()
    {
        HistoryRun run = HistoryRun.Execute(TestWorlds.Standard());
        WorldState world = run.World;

        Assert.NotEmpty(world.HolySites);
        Assert.Contains(world.HolySites, site => site.IsWithinSettlement);
        Assert.Contains(world.HolySites, site => !site.IsWithinSettlement);

        foreach (Religion faith in world.Religions)
        {
            Assert.Contains(world.HolySites, site => site.ReligionId == faith.Id);
        }

        foreach (HolySite site in world.HolySites)
        {
            Assert.True(world.Religions.Contains(site.ReligionId));
            Assert.True(world.Regions.Contains(site.RegionId));

            Region region = world.Regions[site.RegionId];
            Assert.True(
                region.Bounds.Contains(site.X, site.Z),
                $"{site.Name} stands at {site.X}, {site.Z}, outside {region.Id}.");

            if (site.IsWithinSettlement)
            {
                Settlement settlement = world.Settlements[site.SettlementId];
                Assert.Equal(settlement.RegionId, site.RegionId);
                Assert.Equal((settlement.X, settlement.Z), (site.X, site.Z));
            }

            HolySiteDescription description = site.Description;
            Assert.False(string.IsNullOrWhiteSpace(description.Dedication), site.Name);
            Assert.False(string.IsNullOrWhiteSpace(description.Style), site.Name);
            Assert.False(string.IsNullOrWhiteSpace(description.Atmosphere), site.Name);
            Assert.False(string.IsNullOrWhiteSpace(description.Capacity), site.Name);
            Assert.False(string.IsNullOrWhiteSpace(description.FocalPoint), site.Name);
            Assert.False(string.IsNullOrWhiteSpace(description.Offering), site.Name);

            if (!description.DedicateeId.IsNone)
            {
                Assert.True(world.Figures.Contains(description.DedicateeId), site.Name);
                Assert.True(
                    world.Figures[description.DedicateeId].BirthYear < site.FoundedYear,
                    $"{site.Name} honours {description.DedicateeId} who was not yet born.");
            }
        }

        Assert.Equal(
            world.HolySites.Count,
            Of(run, EventKind.HolySiteFounded).Count);
    }

    /// <summary>
    /// A holy place is described in the tongue and climate that raised it, and the wording is
    /// a fact of founding rather than of later export.
    /// </summary>
    [Fact]
    public void HolySiteDescriptionsAreComposedAtFoundingAndStable()
    {
        HistoryRun first = HistoryRun.Execute(TestWorlds.Standard());
        HistoryRun second = HistoryRun.Execute(TestWorlds.Standard());

        Assert.Equal(first.World.HolySites.Count, second.World.HolySites.Count);

        var traditions = new HashSet<SacredTradition>();
        var dedications = new HashSet<HolySiteDedicationKind>();

        foreach (HolySite site in first.World.HolySites)
        {
            HolySite other = second.World.HolySites[site.Id];
            HolySiteDescription a = site.Description;
            HolySiteDescription b = other.Description;

            Assert.Equal(a.Tradition, b.Tradition);
            Assert.Equal(a.DedicationKind, b.DedicationKind);
            Assert.Equal(a.Dedication, b.Dedication);
            Assert.Equal(a.Style, b.Style);
            Assert.Equal(a.Atmosphere, b.Atmosphere);
            Assert.Equal(a.Scale, b.Scale);
            Assert.Equal(a.Capacity, b.Capacity);
            Assert.Equal(a.HasStatue, b.HasStatue);
            Assert.Equal(a.FocalPoint, b.FocalPoint);
            Assert.Equal(a.Offering, b.Offering);
            Assert.Equal(a.DedicateeId, b.DedicateeId);

            traditions.Add(a.Tradition);
            dedications.Add(a.DedicationKind);
        }

        Assert.True(
            traditions.Count >= 2,
            "A standard world should raise holy places in more than one architectural tradition.");
        Assert.True(
            dedications.Count >= 2,
            "A standard world should dedicate holy places to more than one kind of presence.");
    }

    /// <summary>
    /// A holy place does not preach a second religion.
    /// </summary>
    /// <remarks>
    /// Kind, dedication, offering and the words that describe them used to be nominated by
    /// architecture and terrain alone, with the faith's character adding a thumb on the scale.
    /// That left animisms raising churches to saints and dry congregations leaving wine. The
    /// congregation now admits or refuses; this is the property that makes that refusal load-bearing.
    /// </remarks>
    [Fact]
    public void HolySitesAgreeWithTheFaithThatRaisedThem()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HolySite site in world.HolySites)
            {
                Religion faith = world.Religions[site.ReligionId];
                FaithCharacter character = faith.Character;
                HolySiteDescription description = site.Description;

                Assert.True(
                    character.AdmitsKind(site.Kind) || site.Kind == HolySiteKind.Shrine,
                    $"{site.Name} is a {site.Kind}, which the {faith.Name} "
                    + $"({character.Deity}, {character.Authority}) would not raise.");

                Assert.True(
                    character.AdmitsDedication(description.DedicationKind),
                    $"{site.Name} is dedicated to a {description.DedicationKind}, which the "
                    + $"{faith.Name} ({character.Deity}) does not worship.");

                if (character.Diet == DietaryRule.TabooIntoxicants)
                {
                    Assert.DoesNotContain(
                        "wine",
                        description.Offering,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (character.Diet == DietaryRule.TabooFlesh)
                {
                    Assert.DoesNotContain("animal fat", description.Offering, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("fish hooks", description.Offering, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("antlers", description.Offering, StringComparison.OrdinalIgnoreCase);
                }

                if (!character.AdmitsDedication(HolySiteDedicationKind.Saint))
                {
                    Assert.DoesNotContain("saint", description.FocalPoint, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Saint ", description.Dedication, StringComparison.Ordinal);
                }

                if (character.Deity == DeityStructure.Monotheistic)
                {
                    Assert.DoesNotContain("multi-faced", description.FocalPoint, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Ancient God", description.Dedication, StringComparison.Ordinal);
                    Assert.DoesNotContain("nature spirit", description.Dedication, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    /// <summary>
    /// One faith, one sacred place per spot.
    /// </summary>
    /// <remarks>
    /// <para>A settlement can lose its faith to a neighbour's and win it back generations later,
    /// and the founding draws are keyed to the settlement and the faith rather than to the year.
    /// Both possible locations — the settlement's own coordinate, and the independent site chosen
    /// by a deterministic search over the same refined terrain — are pure functions of the
    /// settlement. So the second founding rebuilt the first: same faith, same coordinate, and in
    /// 53 of 55 observed cases the same kind, one temple standing inside another.</para>
    ///
    /// <para>Seeded across a spread because a settlement has to lose and regain a faith for this
    /// to arise at all; seed 42 alone never does it, while 7 and 99 both do.</para>
    /// </remarks>
    [Fact]
    public void NoFaithConsecratesTheSameGroundTwice()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            var consecrated = new HashSet<(EntityId Faith, int X, int Z)>();

            foreach (HolySite site in world.HolySites)
            {
                Assert.True(
                    consecrated.Add((site.ReligionId, site.X, site.Z)),
                    $"On seed {seed}, {site.Name} stands at {site.X}, {site.Z} — ground the "
                    + $"{world.NameOf(site.ReligionId)} had already consecrated.");
            }
        }
    }

    /// <summary>
    /// An artifact is where its provenance says it is.
    /// </summary>
    /// <remarks>
    /// <para>Provenance is append-only and the holder is a field, so the two can drift — and the
    /// paths that move an artifact are spread across a war, a settlement abandonment and a
    /// volcano. This is the assertion that keeps them honest.</para>
    ///
    /// <para>Seeded across a spread rather than run on one world, because the rarest of those
    /// paths is the one most likely to be wrong: a relic conceded at a peace had already been
    /// carried off when its town was sacked, which no seed-42 war happens to do.</para>
    /// </remarks>
    [Fact]
    public void ArtifactProvenanceMatchesWhereTheyAre()
    {
        foreach (ulong seed in Seeds)
        {
            HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(seed));

            Assert.NotEmpty(run.World.Artifacts);
            AssertProvenanceIsHonest(run.World);
        }
    }

    private static void AssertProvenanceIsHonest(WorldState world)
    {
        foreach (Artifact artifact in world.Artifacts)
        {
            Assert.NotEmpty(artifact.Provenance);

            ArtifactHolding last = artifact.Provenance[artifact.Provenance.Count - 1];
            Assert.Equal(last.SettlementId, artifact.HolderId);
            Assert.Equal(last.OwnerId, artifact.OwnerId);

            int previous = int.MinValue;
            EntityId previousPlace = EntityId.None;
            EntityId previousOwner = EntityId.None;

            foreach (ArtifactHolding holding in artifact.Provenance)
            {
                Assert.True(holding.Year >= previous, $"{artifact.Id} moved backwards in time.");

                // Every entry is a change of place or of owner. A no-op entry reads as a second
                // journey the object never made.
                Assert.False(
                    holding.SettlementId == previousPlace && holding.OwnerId == previousOwner
                    && previous != int.MinValue,
                    $"{artifact.Id} arrived at {holding.SettlementId} in {holding.Year}, "
                    + $"claimed by {holding.OwnerId}, where it already was — \"{holding.How}\".");

                previous = holding.Year;
                previousPlace = holding.SettlementId;
                previousOwner = holding.OwnerId;
            }

            if (artifact.IsExtant)
            {
                Assert.False(artifact.HolderId.IsNone);
                Assert.True(
                    world.Settlements[artifact.HolderId].IsActive,
                    $"{artifact.Id} is held by a settlement nobody lives in.");

                if (!artifact.OwnerId.IsNone)
                {
                    Assert.True(world.Figures.Contains(artifact.OwnerId));
                    Assert.True(
                        world.Figures[artifact.OwnerId].IsAlive,
                        $"{artifact.Id} is claimed by the dead {artifact.OwnerId}.");
                }
            }
            else
            {
                Assert.True(artifact.HolderId.IsNone);
                Assert.True(artifact.OwnerId.IsNone);
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
            Assert.InRange(contents.CopyLimit, 0, 5);
            Assert.True(contents.Copies.Count <= contents.CopyLimit);
            Assert.False(contents.SubjectId.IsNone);
            Assert.True(ExistedBy(run.World, contents.SubjectId, artifact.CreatedYear));

            EntityKind expectedSubject = contents.Kind switch
            {
                TomeContentKind.Biography or TomeContentKind.Campaign or TomeContentKind.Itinerary => EntityKind.Figure,
                TomeContentKind.ReligiousRite
                    or TomeContentKind.ReligiousTeaching
                    or TomeContentKind.Cosmology => EntityKind.Religion,
                TomeContentKind.ArtifactHistory => EntityKind.Artifact,
                TomeContentKind.Dedication => EntityKind.HolySite,
                TomeContentKind.RealmChronicle => EntityKind.Civilization,
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
            else if (contents.Kind == TomeContentKind.Dedication)
            {
                if (!contents.ContextId.IsNone)
                {
                    Assert.Equal(EntityKind.Figure, contents.ContextId.Kind);
                    Assert.True(ExistedBy(run.World, contents.ContextId, artifact.CreatedYear));
                }
            }
            else
            {
                Assert.True(contents.ContextId.IsNone);
            }

            foreach (TomeSection section in contents.Sections)
            {
                Assert.False(string.IsNullOrWhiteSpace(section.Heading));
                Assert.False(string.IsNullOrWhiteSpace(section.Text));
                int written = section.Year == 0 ? artifact.CreatedYear : section.Year;
                Assert.InRange(written, artifact.CreatedYear, run.World.EndYear);

                foreach (EntityId reference in section.References)
                {
                    Assert.True(
                        ExistedBy(run.World, reference, written),
                        $"{artifact.Name} written in {written} cites future or missing {reference}.");
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
    /// Famous objects have a living claimant or sit in a treasury, and some books were paid for.
    /// </summary>
    [Fact]
    public void ArtifactsAreClaimedAndSomeBooksAreCommissioned()
    {
        int owned = 0;
        int commissioned = 0;
        int scriptoria = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Artifact artifact in world.Artifacts)
            {
                if (artifact.IsExtant && !artifact.OwnerId.IsNone) owned++;
                if (artifact.Kind == ArtifactKind.Tome && !artifact.CreatorId.IsNone)
                {
                    commissioned++;
                }
            }

            foreach (Settlement settlement in world.Settlements)
            {
                if (settlement.IsActive && Tomes.HasScriptorium(world, settlement)) scriptoria++;
            }
        }

        Assert.True(owned > 0, "No extant artifact was claimed by a person.");
        Assert.True(commissioned > 0, "No book named a patron.");
        Assert.True(scriptoria > 0, "No monastery sat beside a town in the sample.");
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
                Assert.InRange(contents.Copies.Count, 0, 5);
            }
        }

        Assert.True(tomes >= 12, $"Only {tomes} tomes were made; the sample cannot calibrate circulation.");
        Assert.InRange((double)eligible / tomes, 0.55, 0.95);
        Assert.InRange((double)distributed / tomes, 0.45, 0.90);
        Assert.InRange((double)copies / tomes, 0.70, 2.60);
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
            simulator: new Simulator(new ISystem[]
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
        //
        // The allowance is stated as a number of foundings rather than a number of samples,
        // because it is foundings that the flavour systems actually move. It was a flat 200 until
        // M10 gave expansion the same 8x8 refinement a capital gets, which multiplied the cost of
        // a single founding by four and would have made this fail on a change that altered nothing
        // it is watching for.
        const int SamplesPerFounding = 64;
        const int FoundingsOfSlack = 20;

        Assert.True(
            withFlavour <= without + (SamplesPerFounding * FoundingsOfSlack),
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

    private static bool AnyFaithEnded(WorldState world)
    {
        foreach (Religion faith in world.Religions)
        {
            if (!faith.IsActive) return true;
        }

        return false;
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
        EntityKind.HolySite => world.HolySites.Contains(id)
            && world.HolySites[id].FoundedYear <= year,
        _ => false,
    };
}
