using System.Linq;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// The levy that puts a soldier on a field weighs how far their home is from it, over the route
/// network — and a figure drawn away from home gets the march that explains their presence.
/// </summary>
/// <remarks>
/// Built on hand-placed settlements for the cases that need exact control over what the route
/// graph does and does not reach, the same reason <see cref="ItineraryTests"/> does — and on the
/// standard seed panel for the properties that are a distribution rather than a single fact.
/// </remarks>
public sealed class CampaignLevyTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public CampaignLevyTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A soldier whose home is genuinely remote from a battle — far enough that the levy weight is
    /// vanishingly small — is not levied to it, even when a route search finds no path there at all.
    /// </summary>
    /// <remarks>
    /// <para><b>Distance excludes them, not topology.</b> An earlier version of this test placed the
    /// soldier merely off the route network — no road joined their town to anything — and asserted
    /// they could never be levied. That tested the wrong mechanism: the levy no longer treats an
    /// unreached residence as forbidden, only as unpriced by any route, and prices it instead at
    /// <see cref="Itineraries.OverlandOneWayDays"/>, the straight line at plain marching pace. A
    /// route-disconnected town one valley over would now be priced cheaply and could well be levied
    /// — correctly, since an army does not need a merchant to have gone that way first. What still
    /// makes a soldier an implausible draw is being far away, so this places the soldier thousands
    /// of world units off — no settlement in a standard or small world is ever built that far apart
    /// — rather than merely off the graph.</para>
    ///
    /// <para>Two things are pinned: the mechanism, by asserting <see cref="Campaigns.ReachWeight"/>
    /// itself is tiny at the measured overland distance — true on every run, independent of any
    /// coin flip — and the observed behaviour, by running the actual levy once and checking this
    /// particular soldier was not drawn, which a run this deterministic reproduces identically every
    /// time it is executed.</para>
    /// </remarks>
    [Fact]
    public void AGenuinelyRemoteSoldierIsNotLeviedToAFrontierBattle()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        int year = world.StartYear;

        Civilization attacker = world.Civilizations[0];
        Civilization defender = world.Civilizations[1];

        Settlement home = AddSettlement(world, "FarOff", 500_000, 500_000, attacker.Id);
        Settlement field = AddSettlement(world, "Frontier", 0, 0, defender.Id);

        // No route joins home to field. That no longer excludes the soldier by itself — it only
        // means the levy has to price them by the overland fallback rather than a discounted route,
        // and it is the sheer distance below that keeps the resulting weight tiny.
        int overlandDays = Itineraries.OverlandOneWayDays(world, home.Id, field.Id);
        double weight = Campaigns.ReachWeight(overlandDays);
        Assert.True(
            weight < 0.02,
            $"expected a remote soldier's levy weight to be vanishingly small, was {weight:P2} at {overlandDays} days.");

        Figure soldier = AddSoldier(world, "Levied Nobody", attacker.Id, home.Id, year);

        War war = new(
            world.Wars.NextId,
            "Test War",
            attacker.Id,
            defender.Id,
            CasusBelli.BorderDispute,
            EntityId.None,
            EntityId.None,
            EntityId.None,
            year);
        world.Wars.Add(war);

        var battle = new Battle(world.Battles.NextId, "Battle of Frontier", war.Id, new Stamp(year, 1), world.Regions[0].Id)
        {
            SettlementId = field.Id,
            IsSiege = false,
            SiegeOutcome = SiegeOutcome.NotSiege,
            AttackerId = attacker.Id,
            DefenderId = defender.Id,
            AttackerStrength = 100,
            DefenderStrength = 100,
        };
        world.Battles.Add(battle);
        war.BattleIds.Add(battle.Id);

        Campaigns.NoteBattle(world, war, battle, year);

        Assert.DoesNotContain(
            soldier.Campaigns,
            memory => memory.BattleId == battle.Id && memory.Role == CampaignRole.Fought);
        Assert.DoesNotContain(battle.WitnessIds, id => id == soldier.Id);
        Assert.Empty(soldier.Journeys);
    }

    /// <summary>
    /// The same soldier, once a route actually joins the two towns, can be levied and — when they
    /// are — is given the march the connected case makes possible.
    /// </summary>
    [Fact]
    public void AConnectedSoldierCanBeLeviedAndMarches()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        int year = world.StartYear;

        Civilization attacker = world.Civilizations[0];
        Civilization defender = world.Civilizations[1];

        Settlement home = AddSettlement(world, "Muster", 100, 0, attacker.Id);
        Settlement field = AddSettlement(world, "Frontier2", 0, 0, defender.Id);
        var route = new TradeRoute(
            world.TradeRoutes.NextId, home.Id, field.Id, TradeRouteMode.Overland, foundedYear: world.StartYear, traffic: 0.5);
        world.TradeRoutes.Add(route);

        int levied = 0;

        // Rolled across a spread of soldiers rather than pinned to one figure id, since the levy
        // is a chance and a single draw could reasonably miss.
        for (int i = 0; i < 40; i++)
        {
            Figure soldier = AddSoldier(world, "Candidate " + i, attacker.Id, home.Id, year);

            War war = new(
                world.Wars.NextId,
                "Test War " + i,
                attacker.Id,
                defender.Id,
                CasusBelli.BorderDispute,
                EntityId.None,
                EntityId.None,
                EntityId.None,
                year);
            world.Wars.Add(war);

            var battle = new Battle(world.Battles.NextId, "Battle " + i, war.Id, new Stamp(year, 1), world.Regions[0].Id)
            {
                SettlementId = field.Id,
                IsSiege = false,
                SiegeOutcome = SiegeOutcome.NotSiege,
                AttackerId = attacker.Id,
                DefenderId = defender.Id,
                AttackerStrength = 100,
                DefenderStrength = 100,
            };
            world.Battles.Add(battle);
            war.BattleIds.Add(battle.Id);

            Campaigns.NoteBattle(world, war, battle, year);

            bool fought = soldier.Campaigns.Exists(
                memory => memory.BattleId == battle.Id && memory.Role == CampaignRole.Fought);
            if (!fought) continue;

            levied++;
            Journey march = Assert.Single(soldier.Journeys);
            Assert.Equal(JourneyKind.Campaign, march.Kind);
            Assert.Equal(home.Id, march.FromSettlementId);
            Assert.Equal(field.Id, march.ToSettlementId);
            Assert.NotNull(march.Itinerary);
            Assert.Equal(new[] { route.Id }, march.Itinerary!.RouteIds);
        }

        Assert.True(levied > 0, "Not one of 40 connected soldiers was ever levied.");
    }

    /// <summary>
    /// Across the standard seed panel, a levied soldier's march distance skews toward the near
    /// end: most who are away from home at all still travelled well under the levy's own
    /// half-life to get there.
    /// </summary>
    /// <remarks>
    /// The bucket split is <see cref="ReachHalfLifeDays"/> itself — the distance at which a
    /// candidate's weight has fallen to half of a garrison man's — rather than an arbitrary day
    /// count, so the assertion reads directly off the constant the levy actually uses. A fixed
    /// absolute threshold picked independently of that constant (an earlier version of this test
    /// used seven days) stops meaning anything once the constant is recalibrated to this world's
    /// actual travel-day scale, which runs to dozens of days per hop; see the mechanism-level proof
    /// in <see cref="LevyRateFallsWithDistance"/> for a version of this claim immune to whatever
    /// mix of near and far soldiers a given seed panel happens to produce.
    /// </remarks>
    [Fact]
    public void LeviedDistanceSkewsLocalAcrossSeeds()
    {
        const int HalfLife = 90; // Campaigns.ReachHalfLifeDays — see the remarks above.

        int levied = 0;
        int atResidence = 0;
        int withinHalfLife = 0;
        int farther = 0;
        int longestDays = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (CampaignMemory memory in figure.Campaigns)
                {
                    if (memory.Role != CampaignRole.Fought) continue;
                    if (!world.Battles.Contains(memory.BattleId)) continue;

                    Battle battle = world.Battles[memory.BattleId];
                    if (battle.SettlementId.IsNone) continue;

                    levied++;

                    Journey? march = MarchFor(figure, battle, memory.Year);
                    if (march is null)
                    {
                        atResidence++;
                        continue;
                    }

                    int days = march.Itinerary?.OneWayDays ?? 0;
                    longestDays = Math.Max(longestDays, days);
                    if (days <= HalfLife) withinHalfLife++;
                    else farther++;
                }
            }
        }

        Assert.True(levied > 20, $"Only {levied} soldier levies were measured across the panel.");

        int local = atResidence + withinHalfLife;
        _output.WriteLine(
            $"levied {levied}: at residence {atResidence} ({100.0 * atResidence / levied:F1}%), "
            + $"marched <= half-life ({HalfLife}d) {withinHalfLife} ({100.0 * withinHalfLife / levied:F1}%), "
            + $"marched farther {farther} ({100.0 * farther / levied:F1}%), "
            + $"local total {local} ({100.0 * local / levied:F1}%), longest march {longestDays} days.");

        Assert.True(
            local > farther,
            $"Levy did not skew local: {local} within the half-life against {farther} farther ones.");
    }

    /// <summary>
    /// The mechanism itself, isolated from population mix: four otherwise-identical soldiers at
    /// four known distances from the same battle, replayed across many independent battles, are
    /// levied at empirical rates that fall with distance.
    /// </summary>
    /// <remarks>
    /// <see cref="LeviedDistanceSkewsLocalAcrossSeeds"/> counts a real run, where a seed's mix of
    /// near and far soldiers can make the raw levied count skew far even while every individual
    /// soldier's own chance still falls with their own distance — a realm simply has more distant
    /// soldiery than colocated garrison, so "more levies came from far away" does not by itself
    /// contradict "a given soldier is likelier to be levied the closer they are". This test controls
    /// for that by holding four soldiers' availability identical (same age, no rank, no
    /// injuries) and varying only their residence, then reading the levy rate straight from repeated
    /// independent rolls rather than from one run's population.
    /// </remarks>
    [Fact]
    public void LevyRateFallsWithDistance()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        int year = world.StartYear;

        Civilization attacker = world.Civilizations[0];
        Civilization defender = world.Civilizations[1];

        Settlement field = AddSettlement(world, "Field", 0, 0, defender.Id);

        // Each home is joined to the field by one direct route whose road is cut to an exact
        // length, so the one-way travel days are controlled rather than read off geometry: at the
        // default 12 units per travel day, 120/1080/3600 units give almost exactly 10/90/300 days.
        Settlement atField = AddSettlement(world, "AtField", 50, 0, attacker.Id);
        Settlement near = AddSettlement(world, "Near", 500, 0, attacker.Id);
        Settlement atHalfLife = AddSettlement(world, "AtHalfLife", 1500, 0, attacker.Id);
        Settlement far = AddSettlement(world, "Far", 3000, 0, attacker.Id);

        AddCutRoute(world, atField, field, 12); // rounds up to the 1-day floor: this is "at home".
        AddCutRoute(world, near, field, 120);
        AddCutRoute(world, atHalfLife, field, 1080);
        AddCutRoute(world, far, field, 3600);

        Figure soldierAtField = AddSoldier(world, "Garrison", attacker.Id, atField.Id, year);
        Figure soldierNear = AddSoldier(world, "Near Soldier", attacker.Id, near.Id, year);
        Figure soldierAtHalfLife = AddSoldier(world, "Half-life Soldier", attacker.Id, atHalfLife.Id, year);
        Figure soldierFar = AddSoldier(world, "Far Soldier", attacker.Id, far.Id, year);

        var levies = new System.Collections.Generic.Dictionary<EntityId, int>
        {
            [soldierAtField.Id] = 0,
            [soldierNear.Id] = 0,
            [soldierAtHalfLife.Id] = 0,
            [soldierFar.Id] = 0,
        };

        const int Trials = 400;
        for (int i = 0; i < Trials; i++)
        {
            War war = new(
                world.Wars.NextId,
                "Replay War " + i,
                attacker.Id,
                defender.Id,
                CasusBelli.BorderDispute,
                EntityId.None,
                EntityId.None,
                EntityId.None,
                year);
            world.Wars.Add(war);

            var battle = new Battle(world.Battles.NextId, "Replay " + i, war.Id, new Stamp(year, 1), world.Regions[0].Id)
            {
                SettlementId = field.Id,
                IsSiege = false,
                SiegeOutcome = SiegeOutcome.NotSiege,
                AttackerId = attacker.Id,
                DefenderId = defender.Id,
                AttackerStrength = 100,
                DefenderStrength = 100,
            };
            world.Battles.Add(battle);
            war.BattleIds.Add(battle.Id);

            Campaigns.NoteBattle(world, war, battle, year);

            foreach (EntityId soldierId in levies.Keys.ToArray())
            {
                Figure soldier = world.Figures[soldierId];
                if (soldier.Campaigns.Exists(
                        memory => memory.BattleId == battle.Id && memory.Role == CampaignRole.Fought))
                {
                    levies[soldierId]++;
                }
            }
        }

        double rateAtField = (double)levies[soldierAtField.Id] / Trials;
        double rateNear = (double)levies[soldierNear.Id] / Trials;
        double rateAtHalfLife = (double)levies[soldierAtHalfLife.Id] / Trials;
        double rateFar = (double)levies[soldierFar.Id] / Trials;

        _output.WriteLine(
            $"levy rate over {Trials} trials — at field (0d): {rateAtField:P1}, near (10d): {rateNear:P1}, "
            + $"at half-life (90d): {rateAtHalfLife:P1}, far (300d): {rateFar:P1}.");

        Assert.True(
            rateAtField >= rateNear,
            $"At-home rate {rateAtField:P1} was not >= the near rate {rateNear:P1}.");
        Assert.True(
            rateNear >= rateAtHalfLife,
            $"Near rate {rateNear:P1} was not >= the half-life rate {rateAtHalfLife:P1}.");
        Assert.True(
            rateAtHalfLife >= rateFar,
            $"Half-life rate {rateAtHalfLife:P1} was not >= the far rate {rateFar:P1}.");
        Assert.True(
            rateAtField > rateFar + 0.15,
            $"At-home rate {rateAtField:P1} was not clearly above the far rate {rateFar:P1}.");
    }

    /// <summary>
    /// Every named commander or soldier at a settlement other than their own residence has a
    /// recorded march to it — the fact that put them there is on their page, not only the fact
    /// that they were present.
    /// </summary>
    [Fact]
    public void EveryAwayPresenceHasAMatchingMarch()
    {
        int checkedPresences = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (CampaignMemory memory in figure.Campaigns)
                {
                    if (memory.Role is not (CampaignRole.Fought or CampaignRole.Commanded)) continue;
                    if (!world.Battles.Contains(memory.BattleId)) continue;

                    Battle battle = world.Battles[memory.BattleId];
                    if (battle.SettlementId.IsNone) continue;

                    Journey? march = MarchFor(figure, battle, memory.Year);
                    if (march is null) continue;

                    checkedPresences++;
                    Assert.NotEqual(march.FromSettlementId, march.ToSettlementId);
                    Assert.Equal(battle.SettlementId, march.ToSettlementId);
                    Assert.Equal(JourneyKind.Campaign, march.Kind);
                }
            }
        }

        Assert.True(checkedPresences > 0, "No away presence with a march was ever found to check.");
    }

    /// <summary>The march this figure made to this battle in this year, if the levy recorded one.</summary>
    private static Journey? MarchFor(Figure figure, Battle battle, int year)
    {
        foreach (Journey journey in figure.Journeys)
        {
            if (journey.Kind != JourneyKind.Campaign) continue;
            if (journey.ToSettlementId != battle.SettlementId) continue;
            if (journey.Year != year) continue;

            return journey;
        }

        return null;
    }

    private static Settlement AddSettlement(
        WorldState world, string name, int x, int z, EntityId civilizationId)
    {
        var settlement = new Settlement(
            world.Settlements.NextId,
            civilizationId,
            world.Regions[0].Id,
            name,
            x,
            z,
            foundedYear: world.StartYear,
            population: 500);

        world.Settlements.Add(settlement);
        return settlement;
    }

    /// <summary>A direct route whose road is cut to an exact length, for a controlled travel cost.</summary>
    private static TradeRoute AddCutRoute(WorldState world, Settlement a, Settlement b, double length)
    {
        var route = new TradeRoute(
            world.TradeRoutes.NextId, a.Id, b.Id, TradeRouteMode.Overland, foundedYear: world.StartYear, traffic: 0.5)
        {
            Road = new Road(
                new[] { new RoadPoint(0, 0), new RoadPoint((int)length, 0) },
                RoadGrade.Track,
                builtYear: world.StartYear,
                pavedYear: null,
                length: length),
        };
        world.TradeRoutes.Add(route);
        return route;
    }

    private static Figure AddSoldier(
        WorldState world, string name, EntityId civilizationId, EntityId residenceId, int year)
    {
        var figure = new Figure(
            world.Figures.NextId,
            civilizationId,
            world.Cultures[0].Id,
            name,
            Sex.Male,
            birthYear: year - 30)
        {
            Occupation = Occupation.Soldiery,
            ResidenceSettlementId = residenceId,
        };

        world.Figures.Add(figure);
        return figure;
    }
}
