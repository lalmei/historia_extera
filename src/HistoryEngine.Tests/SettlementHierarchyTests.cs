using System.Collections.Generic;
using HistoryEngine.Entities;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The settlement size distribution, and the two mechanisms that give it a shape.
/// </summary>
/// <remarks>
/// <para>These exist because the hierarchy was upside down for the model's whole life and every
/// test in the suite passed throughout. Carrying capacity was dominated by a flat per-trade
/// constant, so a village handed a specialization was handed a town, and nothing anywhere in the
/// model could keep a settlement small — it could only kill one. Thousand-year runs finished with
/// 66% of their settlements towns on one seed and <em>75% of them cities</em> on another.</para>
///
/// <para>The failure was invisible to unit tests because every part worked: capacity was
/// deterministic, growth was logistic, tiers were assigned correctly from population. What was
/// wrong was a distribution, which is a property of a whole world and has to be asserted as one.
/// </para>
/// </remarks>
public sealed class SettlementHierarchyTests
{
    private static readonly ulong[] Seeds = { 42, 7, 101, 2024, 555 };

    /// <summary>
    /// Most settlements must be small, and the large ones must be few.
    /// </summary>
    /// <remarks>
    /// Deliberately loose. The claim is not that a particular seed produces particular counts — it
    /// is that the world is a hierarchy at all rather than a heap of interchangeable towns. A run
    /// where most places are cities has lost the tier ladder's meaning, and the viewer, the
    /// narration and the plague model all read that ladder.
    /// </remarks>
    [Fact]
    public void MostSettlementsAreSmallerThanTowns()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Long(seed)).World;

            int total = 0;
            int cities = 0;
            int belowTown = 0;

            foreach (Settlement settlement in world.Settlements)
            {
                if (!settlement.IsActive) continue;

                total++;
                if (settlement.Tier == SettlementTier.City) cities++;
                if (settlement.Tier < SettlementTier.Town) belowTown++;
            }

            Assert.True(total > 0, $"Seed {seed} finished with no settlements at all.");

            double cityShare = cities / (double)total;
            Assert.True(
                cityShare <= 0.25,
                $"Seed {seed}: {cities} of {total} settlements ({cityShare:P0}) are cities. A world " +
                "where cities are ordinary has no hierarchy left to report.");

            double smallShare = belowTown / (double)total;
            Assert.True(
                smallShare >= 0.15,
                $"Seed {seed}: only {belowTown} of {total} settlements ({smallShare:P0}) are below " +
                "town. Something is granting capacity that the land and the roads did not earn.");
        }
    }

    /// <summary>
    /// A settlement's neighbours must cost it capacity, and its own growth must not be free.
    /// </summary>
    /// <remarks>
    /// The direct statement of what <see cref="Hinterland"/> is for. Asserted against a surveyed
    /// world rather than a constructed one, because the share depends on real coordinates.
    /// </remarks>
    [Fact]
    public void CrowdedGroundIsSharedAndEmptyGroundIsNot()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;
        Hinterland hinterland = Hinterland.Survey(world);

        var shares = new List<double>();
        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive) continue;

            double share = hinterland.ShareFor(world, settlement);
            Assert.InRange(share, 0.0, 1.0);
            shares.Add(share);
        }

        Assert.NotEmpty(shares);

        double lowest = 1.0;
        double highest = 0.0;
        foreach (double share in shares)
        {
            if (share < lowest) lowest = share;
            if (share > highest) highest = share;
        }

        Assert.True(
            lowest < 0.6,
            $"No settlement anywhere in the world was competing for its land — the lowest share " +
            $"was {lowest:F2}. Either every settlement is isolated or the reach is doing nothing.");

        Assert.True(
            highest > lowest,
            "Every settlement got an identical share, which is a flat tax rather than a hinterland.");
    }

    /// <summary>
    /// Trade must reach carrying capacity, in the direction and the ordering it claims.
    /// </summary>
    /// <remarks>
    /// <see cref="Specializations.ImportReliance"/> is the only route by which the trade network
    /// affects how many people live anywhere. Before it, a settlement on four busy routes and one
    /// at the end of a track had the same ceiling, so this asserts both that the term exists and
    /// that a market town cares about it more than a farming village does.
    /// </remarks>
    [Fact]
    public void LiveRoutesRaiseCapacityAndTradingTownsGainMost()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        Settlement settlement = null!;
        foreach (Settlement candidate in world.Settlements)
        {
            if (candidate.IsActive && candidate.Population > 0) { settlement = candidate; break; }
        }

        Assert.NotNull(settlement);

        Civilization civilization = world.Civilizations[settlement.CivilizationId];
        Culture culture = world.CultureOf(civilization);
        Region region = world.Regions[settlement.RegionId];

        double Capacity(SettlementSpecialization trade, double traffic)
        {
            settlement.Specialization = trade;
            return PopulationSystem.CapacityOf(
                world, civilization, culture, settlement, region, harvest: 0.5, traffic, landShare: 1.0);
        }

        double isolatedMarket = Capacity(SettlementSpecialization.Trade, 0.0);
        double connectedMarket = Capacity(SettlementSpecialization.Trade, 2.0);
        double isolatedFarm = Capacity(SettlementSpecialization.Farming, 0.0);
        double connectedFarm = Capacity(SettlementSpecialization.Farming, 2.0);

        Assert.True(
            connectedMarket > isolatedMarket,
            $"Trade routes did not raise a market town's capacity ({isolatedMarket:F0} to " +
            $"{connectedMarket:F0}).");

        Assert.True(
            connectedMarket - isolatedMarket > connectedFarm - isolatedFarm,
            "A farming village gained as much from the roads as a market town did, so " +
            "ImportReliance is not being read.");
    }

    /// <summary>Sharing the land must lower capacity, never raise it.</summary>
    [Fact]
    public void ASmallerLandShareNeverMeansMoreCapacity()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive) continue;

            Civilization civilization = world.Civilizations[settlement.CivilizationId];
            Culture culture = world.CultureOf(civilization);
            Region region = world.Regions[settlement.RegionId];

            double whole = PopulationSystem.CapacityOf(
                world, civilization, culture, settlement, region, 0.5, 0.0, landShare: 1.0);
            double contested = PopulationSystem.CapacityOf(
                world, civilization, culture, settlement, region, 0.5, 0.0, landShare: 0.25);

            Assert.True(
                contested <= whole,
                $"{settlement.Name} gained capacity by losing land ({whole:F0} to {contested:F0}).");
        }
    }
}
