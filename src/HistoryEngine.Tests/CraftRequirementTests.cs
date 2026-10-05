using HistoryEngine.Entities;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// A trade stands on the crafts it cannot run without (#253): lose them and it works less well,
/// regain them and it recovers.
/// </summary>
public sealed class CraftRequirementTests
{
    /// <summary>
    /// A mine whose forge is fed by road loses yield when the road closes and gets it back when the
    /// road reopens.
    /// </summary>
    /// <remarks>
    /// The crafts are lost the way the model says a place loses them: not by a death in the record
    /// but by the supply going away. A mining town on bare ground keeps a smith only while fuel
    /// reaches it, so closing its routes takes the forge and reopening them restores it.
    /// </remarks>
    [Fact]
    public void ATradeDegradesWhenItsCraftsAreLostAndRecoversWhenTheyReturn()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        int exercised = 0;
        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive) continue;

            SettlementSpecialization trade = settlement.Specialization;
            settlement.Specialization = SettlementSpecialization.Mining;
            try
            {
                List<TradeRoute> routes = TradeRoutes.From(world, settlement.Id).ToList();
                if (routes.Count == 0) continue;

                double connected = Crafts.Shortfall(world, settlement);
                foreach (TradeRoute route in routes) route.EndedYear = world.EndYear;
                double cut = Crafts.Shortfall(world, settlement);
                double cutCapacity = Capacity(world, settlement, cut);
                foreach (TradeRoute route in routes) route.EndedYear = null;

                if (cut <= connected) continue;

                double restored = Crafts.Shortfall(world, settlement);
                Assert.Equal(connected, restored);
                Assert.True(
                    cutCapacity < Capacity(world, settlement, restored),
                    $"{settlement.Name}: losing its crafts cost it no capacity");
                exercised++;
            }
            finally
            {
                settlement.Specialization = trade;
            }
        }

        Assert.True(exercised > 0, "no settlement in the world fed its forge by road");
    }

    /// <summary>Missing every craft costs exactly the penalty on what the trade earns, and the land nothing.</summary>
    [Fact]
    public void TheShortfallDragsTheTradeAndLeavesTheLand()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;
        Settlement settlement = world.Settlements.First(s => s.IsActive);
        settlement.Specialization = SettlementSpecialization.Mining;

        Civilization civilization = world.Civilizations[settlement.CivilizationId];
        Region region = world.Regions[settlement.RegionId];

        SettlementSupport whole = PopulationSystem.SupportFor(
            world, civilization, world.CultureOf(civilization), settlement, region, 0.5, 2.0, 1.0, 0.0);
        SettlementSupport bare = PopulationSystem.SupportFor(
            world, civilization, world.CultureOf(civilization), settlement, region, 0.5, 2.0, 1.0, 1.0);

        double kept = 1.0 - Specializations.CraftShortfallPenalty;
        Assert.Equal(whole.FromLand, bare.FromLand, 9);
        Assert.Equal(whole.FromSite * kept, bare.FromSite, 9);
        Assert.Equal(whole.FromTrade * kept, bare.FromTrade, 9);
    }

    /// <summary>The yearly survey answers exactly what the one-off question does.</summary>
    [Fact]
    public void SurveyedRoutesGiveTheSameShortfallAsTheRouteTable()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;
        RouteNeighbours routes = RouteNeighbours.Survey(world);

        foreach (Settlement settlement in world.Settlements)
        {
            foreach (SettlementSpecialization trade in Enum.GetValues<SettlementSpecialization>())
            {
                SettlementSpecialization kept = settlement.Specialization;
                settlement.Specialization = trade;
                Assert.Equal(Crafts.Shortfall(world, settlement), Crafts.Shortfall(world, settlement, routes));
                settlement.Specialization = kept;
            }
        }
    }

    /// <summary>Every required craft is one a village may hold, so a shortfall is never just size.</summary>
    [Fact]
    public void NoRequiredCraftHasATierFloor()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        foreach (SettlementSpecialization trade in Enum.GetValues<SettlementSpecialization>())
        {
            foreach (Craft craft in Specializations.RequiredCrafts(trade))
            {
                Assert.Contains(
                    world.Settlements,
                    s => s.IsActive && s.Tier < SettlementTier.Town && Crafts.Supports(world, s, craft));
            }
        }
    }

    private static double Capacity(WorldState world, Settlement settlement, double shortfall)
    {
        Civilization civilization = world.Civilizations[settlement.CivilizationId];
        return PopulationSystem.CapacityOf(
            world, civilization, world.CultureOf(civilization), settlement,
            world.Regions[settlement.RegionId], 0.5, 1.0, 1.0, shortfall);
    }
}
