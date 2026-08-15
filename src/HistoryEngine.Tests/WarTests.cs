using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// That diplomacy and war produce outcomes, and that the outcomes are consistent.
/// </summary>
/// <remarks>
/// <para>Written the way Milestone 4's lifecycle tests had to be written: asserting that things
/// <em>happen</em>, not that the code paths exist. Every war mechanism in this milestone existed
/// and was correct for a full day before the first war was ever declared, because contact was
/// defined as a shared border and on a thousand-region world with eight civilizations the first
/// two territories to touch did so in year 201. A test for "the declaration path runs when
/// relations are hostile" would have passed throughout.</para>
///
/// <para>Rates are checked across several seeds rather than one. A single world is a small sample
/// of a stochastic process — seed 1 fights five wars and loses no realm, seed 99 fights
/// twenty-four — so a threshold tight enough to be meaningful on one seed is a threshold that
/// fails on another for no reason anybody can act on.</para>
/// </remarks>
public sealed class WarTests
{
    /// <summary>Seeds sampled where the question is about a rate rather than an invariant.</summary>
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    [Fact]
    public void WarsAreDeclaredFoughtAndSettled()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        Assert.True(world.Wars.Count > 0, "Three centuries produced no war at all.");
        Assert.True(world.Battles.Count > 0, "Wars were declared but nothing was ever fought.");

        int settled = 0;
        foreach (War war in world.Wars)
        {
            if (war.IsActive) continue;

            settled++;
            Assert.NotEqual(WarOutcome.Ongoing, war.Outcome);
        }

        Assert.True(settled > 0, "No war ever ended.");
    }

    /// <summary>
    /// Every grievance must be reachable, and alliances must actually be worth something.
    /// </summary>
    /// <remarks>
    /// Each casus belli comes from a different corner of the simulation — a dynastic claim needs a
    /// living marriage between two ruling houses, a revanche needs territory lost in an earlier
    /// war, a relic claim needs a particular object in the other treasury, and a religious war
    /// needs two faiths plus piety and fervour — so one of them silently never firing is the
    /// likeliest way this system rots. The same goes for a pact that is never called: an alliance
    /// nobody answers is a number in the export rather than a thing that happens.
    /// </remarks>
    [Fact]
    public void EveryGrievanceAndTheCallToArmsAllOccur()
    {
        var causes = new Dictionary<CasusBelli, int>();
        int alliances = 0;
        int joined = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (War war in world.Wars)
            {
                causes[war.Cause] = causes.TryGetValue(war.Cause, out int n) ? n + 1 : 1;
                joined += war.Attackers.Count + war.Defenders.Count - 2;
                ValidateReligiousGrievance(world, war);
            }

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind == EventKind.AllianceFormed) alliances++;
            }
        }

        foreach (CasusBelli cause in new[]
        {
            CasusBelli.BorderDispute, CasusBelli.Conquest,
            CasusBelli.DynasticClaim, CasusBelli.Revanche,
            CasusBelli.RelicClaim, CasusBelli.ReligiousWar,
        })
        {
            Assert.True(
                causes.TryGetValue(cause, out int count) && count > 0,
                $"No war anywhere was declared over {cause}, so that claim is unreachable.");
        }

        Assert.False(causes.ContainsKey(CasusBelli.Unknown), "A war was declared for no stated reason.");
        Assert.True(alliances > 0, "No alliance was ever sworn.");
        Assert.True(joined > 0, "No ally ever answered a call to arms.");
    }

    /// <summary>
    /// Where an object was as of a given year, from its provenance.
    /// </summary>
    /// <remarks>
    /// Provenance is append-only and in order, so the last entry at or before the year is where
    /// the object then was. Asking the object's current holder instead answers a different
    /// question, and one no assertion about a peace treaty wants: everything that happened to it
    /// in the intervening centuries.
    /// </remarks>
    private static EntityId HeldAfter(Artifact artifact, int year)
    {
        EntityId held = EntityId.None;

        foreach (ArtifactHolding moment in artifact.Provenance)
        {
            if (moment.Year > year) break;
            held = moment.SettlementId;
        }

        return held;
    }

    /// <summary>
    /// Who held a region in a given year, replayed from the chronicle.
    /// </summary>
    /// <remarks>
    /// The same reasoning as <see cref="HeldAfter"/>, applied to ground rather than to objects, and
    /// the same reasoning the engine itself uses: territory at any year is replayed from the events
    /// that moved it, never read off the end state. Every acre a realm ever held entered the log as
    /// a claim or a cession, so those two kinds are the whole history of a region's ownership.
    /// </remarks>
    private static EntityId OwnerAt(WorldState world, EntityId regionId, int year)
    {
        EntityId owner = EntityId.None;

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Year > year) break;
            if (entry.Subject != regionId) continue;

            if (entry.Kind is EventKind.RegionClaimed or EventKind.RegionCeded)
            {
                owner = entry.Object;
            }
        }

        return owner;
    }

    /// <summary>Religious grievances must preserve the concrete thing or faiths fought over.</summary>
    private static void ValidateReligiousGrievance(WorldState world, War war)
    {
        if (war.Cause == CasusBelli.RelicClaim)
        {
            Assert.True(world.Artifacts.Contains(war.ClaimedRelicId));

            Artifact relic = world.Artifacts[war.ClaimedRelicId];
            Assert.Equal(ArtifactKind.Relic, relic.Kind);
            Assert.True(relic.CreatedYear <= war.StartYear);
            Assert.Equal(EntityId.None, war.AggressorReligionId);
            Assert.Equal(EntityId.None, war.DefenderReligionId);

            HistoryEvent declaration = DeclarationOf(world, war);
            Assert.Contains(war.ClaimedRelicId, declaration.Extra ?? Array.Empty<EntityId>());
            Assert.Contains(relic.Name, declaration.DataValue("cause"));

            if (war.Outcome == WarOutcome.AggressorVictory && war.EndYear is int ended)
            {
                // The relic is the term of peace, not an unrelated strip of frontier.
                Assert.Empty(war.CededRegionIds);

                if (relic.LostYear is not int lost || lost > ended)
                {
                    // The war got what it was declared for — asserted against where the relic was
                    // when the peace was signed, not against where it is at the end of the run.
                    // A relic won in 249 and lost in 290 satisfies the claim and then stops having
                    // a holder at all; checking the end state called that a failed war.
                    EntityId held = HeldAfter(relic, ended);

                    Assert.True(
                        world.Settlements.Contains(held),
                        $"{relic.Name} was claimed by {ended} and held by nothing.");

                    // Whose the holding town was at the peace, not whose it is now. A settlement
                    // won in one war can be lost in the next, and reading its present owner made
                    // this assert that no relic-winner ever subsequently lost the town — which is
                    // not a property of the relic system and not true. The same mistake the
                    // comment above records for the relic, one line further down.
                    Assert.Equal(
                        war.AggressorId,
                        OwnerAt(world, world.Settlements[held].RegionId, ended));
                }
            }

            return;
        }

        Assert.Equal(EntityId.None, war.ClaimedRelicId);

        if (war.Cause != CasusBelli.ReligiousWar)
        {
            Assert.Equal(EntityId.None, war.AggressorReligionId);
            Assert.Equal(EntityId.None, war.DefenderReligionId);
            return;
        }

        Assert.True(world.Religions.Contains(war.AggressorReligionId));
        Assert.True(world.Religions.Contains(war.DefenderReligionId));
        Assert.NotEqual(war.AggressorReligionId, war.DefenderReligionId);

        HistoryEvent holyWarDeclaration = DeclarationOf(world, war);
        Assert.Contains(war.AggressorReligionId, holyWarDeclaration.Extra ?? Array.Empty<EntityId>());
        Assert.Contains(war.DefenderReligionId, holyWarDeclaration.Extra ?? Array.Empty<EntityId>());
    }

    private static HistoryEvent DeclarationOf(WorldState world, War war)
    {
        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind == EventKind.WarDeclared && entry.Location == war.Id) return entry;
        }

        throw new InvalidOperationException($"{war.Name} has no declaration event.");
    }

    /// <summary>A battle is fought by two realms, at a place, and one of them wins it.</summary>
    [Fact]
    public void EveryBattleIsWellFormed()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        foreach (Battle battle in world.Battles)
        {
            Assert.True(world.Wars.Contains(battle.WarId), $"{battle.Name} belongs to no war.");
            Assert.True(world.Regions.Contains(battle.RegionId), $"{battle.Name} was fought nowhere.");

            Assert.NotEqual(battle.AttackerId, battle.DefenderId);
            Assert.True(
                battle.VictorId == battle.AttackerId || battle.VictorId == battle.DefenderId,
                $"{battle.Name} was won by somebody who was not there.");

            Assert.True(
                battle.AttackerLosses <= battle.AttackerStrength
                && battle.DefenderLosses <= battle.DefenderStrength,
                $"{battle.Name} killed more men than either side brought.");

            // A siege is a battle at a settlement; a settlement is not necessarily a siege.
            if (battle.IsSiege) Assert.True(world.Settlements.Contains(battle.SettlementId));
            if (battle.Sacked) Assert.True(world.Settlements.Contains(battle.SettlementId));

            War war = world.Wars[battle.WarId];
            Assert.Contains(battle.Id, war.BattleIds);
            Assert.True(
                war.Involves(battle.AttackerId) && war.Involves(battle.DefenderId),
                $"{battle.Name} was fought by realms that were not at war.");
        }
    }

    /// <summary>
    /// Two engagements at the same place must be told apart by their names.
    /// </summary>
    /// <remarks>
    /// <para>Per place, not per world. Battle names are built from settlement and region names,
    /// and those are deliberately not unique — a name is a pure function of an entity's id, and
    /// deduplicating would make one settlement's name depend on which names earlier settlements
    /// had taken. Two towns eight centuries and half a continent apart can both be Puolijoki, and
    /// a battle at each is genuinely the Battle of Puolijoki, exactly as real geography manages
    /// it. The viewer keys on ids.</para>
    ///
    /// <para>What must hold is that the ordinal counts engagements at the same <em>place</em>
    /// rather than under the same wording. Counting the wording renames somewhere mid-war: a town
    /// sacked out of its tier stops qualifying as a siege, and the next engagement at the same
    /// walls appears as a first battle somewhere apparently new.</para>
    /// </remarks>
    [Fact]
    public void BattlesAtOnePlaceAreNumbered()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard() with { Years = 800 }).World;

        var seen = new HashSet<(EntityId Region, string Name)>();
        int contested = 0;

        foreach (Battle battle in world.Battles)
        {
            Assert.True(
                seen.Add((battle.RegionId, battle.Name)),
                $"Two engagements in {world.NameOf(battle.RegionId)} are both called " +
                $"'{battle.Name}'.");
        }

        var perRegion = new Dictionary<EntityId, int>();
        foreach (Battle battle in world.Battles)
        {
            perRegion[battle.RegionId] =
                perRegion.TryGetValue(battle.RegionId, out int n) ? n + 1 : 1;
        }

        foreach (KeyValuePair<EntityId, int> place in perRegion)
        {
            if (place.Value > 1) contested++;
        }

        Assert.True(world.Battles.Count > 20, "Too few battles in eight centuries to judge naming.");
        Assert.True(contested > 0, "No ground was ever fought over twice, so no ordinal was tested.");
    }

    /// <summary>
    /// Land taken at a peace must change hands completely, settlements included.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is quiet and total. Ceding a region without the town
    /// standing in it leaves that town feeding the loser's population and counting against its
    /// fall condition, so a realm can be stripped of every province it holds and never actually
    /// lose anything.
    /// </remarks>
    [Fact]
    public void CededTerritoryTakesItsSettlementsWithIt()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard() with { Years = 800 }).World;

        int ceded = 0;

        foreach (War war in world.Wars)
        {
            foreach (EntityId regionId in war.CededRegionIds)
            {
                ceded++;
                Region region = world.Regions[regionId];

                if (region.Owner.IsNone) continue;

                Civilization owner = world.Civilizations[region.Owner];
                Assert.Contains(regionId, owner.TerritoryRegionIds);

                foreach (Settlement settlement in world.Settlements)
                {
                    if (!settlement.IsActive || settlement.RegionId != regionId) continue;

                    Assert.True(
                        settlement.CivilizationId == region.Owner,
                        $"{settlement.Name} stands in land held by {owner.Name} but answers to " +
                        $"{world.NameOf(settlement.CivilizationId)}.");

                    Assert.Contains(settlement.Id, owner.SettlementIds);
                }
            }
        }

        Assert.True(ceded > 0, "Eight centuries of war moved no border.");
    }

    /// <summary>Every realm's settlement list and every settlement's owner must agree.</summary>
    [Fact]
    public void OwnershipStaysConsistentAfterConquest()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard() with { Years = 800 }).World;

        foreach (Civilization civilization in world.Civilizations)
        {
            foreach (EntityId id in civilization.SettlementIds)
            {
                Assert.True(
                    world.Settlements[id].CivilizationId == civilization.Id,
                    $"{civilization.Name} lists {world.NameOf(id)}, which belongs to somebody else.");
            }
        }

        int capitals = 0;

        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsCapital) continue;

            capitals++;
            Civilization owner = world.Civilizations[settlement.CivilizationId];
            Assert.True(
                owner.CapitalId == settlement.Id,
                $"{settlement.Name} thinks it is a seat of government and {owner.Name} disagrees.");
        }

        Assert.True(capitals > 0, "Nowhere is anybody's capital.");
    }

    /// <summary>A decisive war moves a border; a stalemate does not.</summary>
    [Fact]
    public void OnlyDecidedWarsTransferTerritory()
    {
        int decisive = 0;
        int withSpoils = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (War war in world.Wars)
            {
                if (war.Outcome == WarOutcome.Stalemate)
                {
                    Assert.Empty(war.CededRegionIds);
                    continue;
                }

                if (war.IsActive) continue;

                decisive++;
                if (war.CededRegionIds.Count > 0) withSpoils++;
            }
        }

        Assert.True(decisive > 0, "No war anywhere was decided.");
        Assert.True(
            withSpoils > decisive / 2,
            $"Only {withSpoils} of {decisive} decided wars took any land, so victory means nothing.");
    }

    /// <summary>
    /// Nobody fights an ally, nobody fights themselves, and nobody fights two wars at once.
    /// </summary>
    /// <remarks>
    /// The middle one is not paranoia. A chain of pacts — A allied to B, B allied to C, C at war
    /// with A — will happily call C to both sides of the same war unless the call checks for it,
    /// and a realm listed in both coalitions makes every strength calculation in the war
    /// meaningless without ever throwing.
    /// </remarks>
    [Fact]
    public void CoalitionsAreCoherent()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (War war in world.Wars)
            {
                foreach (EntityId id in war.Attackers)
                {
                    Assert.DoesNotContain(id, war.Defenders);
                }

                Assert.Equal(war.Attackers.Count, Distinct(war.Attackers));
                Assert.Equal(war.Defenders.Count, Distinct(war.Defenders));
            }

            // At most one running war per realm, at every point the table can be read.
            foreach (Civilization civilization in world.Civilizations)
            {
                int running = 0;
                foreach (War war in world.ActiveWars())
                {
                    if (war.Involves(civilization.Id)) running++;
                }

                Assert.True(running <= 1, $"{civilization.Name} is fighting {running} wars at once.");
            }
        }

        static int Distinct(IReadOnlyList<EntityId> ids)
        {
            var seen = new List<EntityId>(ids.Count);
            foreach (EntityId id in ids)
            {
                if (!seen.Contains(id)) seen.Add(id);
            }

            return seen.Count;
        }
    }

    /// <summary>
    /// A peace must hold for a while, and a realm must not fight the ally it just swore to.
    /// </summary>
    /// <remarks>
    /// Without the truce, the loser's collapsed opinion of the winner re-declares the same war the
    /// following spring. Wars then run continuously, every chronicle reads as one unbroken
    /// campaign, and neither exhaustion nor recovery ever appears — so this checks the gap
    /// directly rather than trusting the constant.
    /// </remarks>
    [Fact]
    public void PeaceHoldsAndAlliesAreNotAttacked()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard() with { Years = 800 }).World;

        var lastPeace = new Dictionary<(EntityId, EntityId), int>();
        int repeats = 0;

        foreach (War war in world.Wars)
        {
            (EntityId, EntityId) pair = war.AggressorId.CompareTo(war.DefenderId) < 0
                ? (war.AggressorId, war.DefenderId)
                : (war.DefenderId, war.AggressorId);

            if (lastPeace.TryGetValue(pair, out int ended))
            {
                repeats++;
                Assert.True(
                    war.StartYear - ended >= 12,
                    $"{world.NameOf(war.AggressorId)} and {world.NameOf(war.DefenderId)} were at " +
                    $"war again {war.StartYear - ended} years after making peace.");
            }

            if (war.EndYear is int year) lastPeace[pair] = year;
        }

        Assert.True(repeats > 0, "No pair of realms ever fought twice, so the truce is untested.");

        foreach (Civilization civilization in world.Civilizations)
        {
            foreach (KeyValuePair<EntityId, int> pact in civilization.Allies)
            {
                Assert.True(
                    world.Civilizations[pact.Key].Allies.ContainsKey(civilization.Id),
                    $"{civilization.Name} counts {world.NameOf(pact.Key)} an ally and is not counted back.");

                Assert.Null(Diplomacy.WarBetween(world, civilization.Id, pact.Key));
            }
        }
    }

    /// <summary>
    /// Conquest must be able to finish a civilization off.
    /// </summary>
    /// <remarks>
    /// The gap this milestone existed to close. Climate alone cannot end a realm — its capital
    /// sits on the best land it could find and carries a capacity bonus, so it sheds its marginal
    /// holdings and keeps its seat — and through Milestone 5 a three-century world lost nobody.
    /// </remarks>
    [Fact]
    public void ARealmCanBeConqueredOutOfExistence()
    {
        int conquered = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind != EventKind.CivilizationFell || entry.Object.IsNone) continue;

                conquered++;

                Civilization fallen = world.Civilizations[entry.Subject];
                Assert.False(fallen.IsActive);
                Assert.True(
                    Realms.IsFinished(world, fallen),
                    $"{fallen.Name} was conquered while it still held a settlement.");

                Assert.True(
                    world.Civilizations.Contains(entry.Object),
                    "A realm was conquered by something that is not a realm.");
            }
        }

        Assert.True(conquered > 0, "No realm anywhere was ever conquered out of existence.");
    }

    [Fact]
    public void OpinionsStayInRangeAndAreNeverHeldAboutOneself()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Civilization civilization in world.Civilizations)
            {
                foreach (KeyValuePair<EntityId, double> relation in civilization.Relations)
                {
                    Assert.InRange(relation.Value, -1.0, 1.0);
                    Assert.NotEqual(civilization.Id, relation.Key);
                }
            }
        }
    }

    /// <summary>
    /// A peace must cost the loser more goodwill than the winner.
    /// </summary>
    /// <remarks>
    /// <para>The asymmetry is the whole reason relations are directed rather than shared, and it
    /// is what sends a beaten realm back for its province a generation later instead of leaving
    /// two neighbours permanently level along the same border.</para>
    ///
    /// <para>Measured as the movement across the year of the treaty, not as who dislikes whom at
    /// the end of the run — which is what this test asked first, and it fails. By year 300 the
    /// grudge has long since drifted back toward whatever the geography says, and the winner is
    /// usually the more aggressive of the two and so structurally the colder. Both facts are
    /// correct; neither is evidence about the peace terms. The step change is.</para>
    /// </remarks>
    [Fact]
    public void APeaceCostsTheLoserMore()
    {
        WorldConfig config = TestWorlds.Standard() with { Years = 800 };

        WorldState world = WorldBuilder.Create(
            config,
            new CountingTerrainSampler(
                new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain)));

        var simulator = new Simulator();

        int compared = 0;
        int loserFellFurther = 0;
        int read = 0;

        while (world.Year <= world.EndYear)
        {
            Dictionary<(EntityId, EntityId), double> before = Snapshot(world);
            simulator.Advance(world, 1);

            for (int i = read; i < world.Chronicle.Count; i++)
            {
                HistoryEvent entry = world.Chronicle.Events[i];
                if (entry.Kind != EventKind.WarEnded || entry.Object.IsNone) continue;

                War war = world.Wars[entry.Subject];
                EntityId winnerId = entry.Object;
                EntityId loserId = war.IsAttacker(winnerId) ? war.DefenderId : war.AggressorId;

                Civilization winner = world.Civilizations[winnerId];
                Civilization loser = world.Civilizations[loserId];

                double loserDrop = before.GetValueOrDefault((loserId, winnerId))
                                   - Diplomacy.Relation(loser, winner);

                double winnerDrop = before.GetValueOrDefault((winnerId, loserId))
                                    - Diplomacy.Relation(winner, loser);

                compared++;
                if (loserDrop > winnerDrop) loserFellFurther++;
            }

            read = world.Chronicle.Count;
        }

        Assert.True(compared > 10, $"Only {compared} decided wars, too few to judge the grudge.");
        Assert.True(
            loserFellFurther > compared * 3 / 4,
            $"The loser gave up more ground in opinion in only {loserFellFurther} of {compared} " +
            "peace settlements, so the terms are effectively symmetric.");

        static Dictionary<(EntityId, EntityId), double> Snapshot(WorldState world)
        {
            var taken = new Dictionary<(EntityId, EntityId), double>();

            foreach (Civilization civilization in world.Civilizations)
            {
                foreach (KeyValuePair<EntityId, double> relation in civilization.Relations)
                {
                    taken[(civilization.Id, relation.Key)] = relation.Value;
                }
            }

            return taken;
        }
    }

    /// <summary>A realm that has not met another has no opinion of it and cannot fight it.</summary>
    [Fact]
    public void RealmsOutOfReachHaveNoDiplomacy()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        foreach (Civilization civilization in world.Civilizations)
        {
            foreach (KeyValuePair<EntityId, double> relation in civilization.Relations)
            {
                Civilization other = world.Civilizations[relation.Key];

                // Contact is remembered, so this is about whether they were ever within reach —
                // which for a realm that has since fallen or moved cannot be re-measured. Both
                // still standing is the case the invariant can be checked on.
                if (!civilization.IsActive || !other.IsActive) continue;

                Assert.True(
                    Diplomacy.Proximity(world, civilization, other) < double.PositiveInfinity,
                    $"{civilization.Name} holds an opinion of a realm with nowhere to hold it about.");
            }
        }
    }

    /// <summary>Sacking must cost a settlement real people, and must not empty it outright.</summary>
    [Fact]
    public void SacksAreCostlyAndSurvivable()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard() with { Years = 800 }).World;

        int sacked = 0;

        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind != EventKind.SettlementSacked) continue;

            sacked++;
            Assert.True(int.TryParse(entry.DataValue("lost"), out int lost) && lost > 0);

            Settlement target = world.Settlements[entry.Subject];
            Assert.True(
                lost < target.PeakPopulation,
                $"{target.Name} lost more people to one sack than it ever had.");
        }

        Assert.True(sacked > 0, "Eight centuries of war sacked nothing.");
    }

    /// <summary>
    /// War must not cost a single terrain sample.
    /// </summary>
    /// <remarks>
    /// Every question these systems ask about the land — how far apart two realms are, how
    /// defensible a province is, which frontier is worth taking — is answered from region
    /// statistics derived once when the world was built. Adding two yearly systems that both
    /// reason about geography and spending nothing to do it is the terrain discipline working, and
    /// worth pinning: the obvious implementation of "how defensible is this ground" reaches
    /// straight for the sampler.
    /// </remarks>
    [Fact]
    public void WarSpendsNoTerrainSamples()
    {
        WorldConfig config = TestWorlds.Standard();

        HistoryRun withWar = HistoryRun.Execute(config);

        var counter = new CountingTerrainSampler(
            new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain));

        WorldState quiet = WorldBuilder.Create(config, counter);
        new Simulator(NoWarSystems()).Run(quiet);

        Assert.True(withWar.World.Wars.Count > 0, "The compared run fought no wars.");

        // Allowed to differ by a few foundings' worth, and no more. War changes which realms
        // survive to expand, and expansion is what costs samples — so the two runs legitimately
        // found different numbers of settlements. What this is watching for is a war system that
        // asks the terrain how defensible a battlefield is, which would cost per battle per year
        // and run to the tens of thousands. The bound was a strict inequality until M10 raised the
        // cost of one founding fourfold, at which point a handful of extra colonies was enough to
        // trip it.
        const int SamplesPerFounding = 64;
        const int FoundingsOfSlack = 20;

        Assert.True(
            withWar.SimulationSamples <= counter.SampleCount + (SamplesPerFounding * FoundingsOfSlack),
            $"A run with war cost {withWar.SimulationSamples} terrain samples against " +
            $"{counter.SampleCount} without it, so the war systems are sampling terrain.");
    }

    private static ISystem[] NoWarSystems()
    {
        var kept = new List<ISystem>();

        foreach (ISystem system in Simulator.DefaultSystems())
        {
            if (system.Name is "diplomacy" or "war") continue;

            kept.Add(system);
        }

        return kept.ToArray();
    }
}
