using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Turns population changes into events: promotions, decline, fortification, abandonment, and
/// the fall of civilizations that lose everything.
/// </summary>
/// <remarks>
/// Runs immediately after <see cref="PopulationSystem"/>, and depends on that ordering — it
/// reads the populations written this year. That dependency is why
/// <see cref="Simulator.SystemOrder"/> is part of the config hash.
///
/// <para>Nothing is ever deleted. An abandoned settlement keeps its id and gains an
/// <see cref="Settlement.AbandonedYear"/>; a fallen civilization keeps its entire history and
/// gains an <see cref="Civilization.EndedYear"/>. Every event that referenced them still
/// resolves in the viewer, which is the whole point of a chronicle.</para>
/// </remarks>
public sealed class SettlementLifecycleSystem : IYearSystem
{
    /// <summary>Population at or below which a settlement is given up.</summary>
    private const int AbandonmentThreshold = 12;

    /// <summary>Yearly chance a town or city builds walls, scaled by its culture's aggression.</summary>
    private const double FortificationChance = 0.04;

    public string Name => "settlement-lifecycle";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);
            int survivingSettlements = 0;

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                if (settlement.Population <= AbandonmentThreshold)
                {
                    Abandon(world, settlement, year);
                    continue;
                }

                survivingSettlements++;
                ApplyTierChange(world, settlement, year);
                MaybeFortify(world, settlement, culture, year, rng);
            }

            if (survivingSettlements == 0)
            {
                Fall(world, civilization, year);
            }
        }
    }

    private static void ApplyTierChange(WorldState world, Settlement settlement, int year)
    {
        SettlementTier tier = SettlementTiers.ForPopulation(settlement.Population);
        if (tier == settlement.Tier) return;

        EventKind kind = tier > settlement.Tier
            ? EventKind.SettlementPromoted
            : EventKind.SettlementDeclined;

        settlement.Tier = tier;

        world.Chronicle.Record(
            year,
            kind,
            settlement.Id,
            obj: settlement.CivilizationId,
            location: settlement.RegionId,
            data: Chronicle.Data(
                ("tier", SettlementTiers.Label(tier)),
                ("population", settlement.Population.ToString(CultureInfo.InvariantCulture))));
    }

    private static void MaybeFortify(
        WorldState world, Settlement settlement, Culture culture, int year, IRng rng)
    {
        if (settlement.IsFortified || settlement.Tier < SettlementTier.Town) return;

        double chance = FortificationChance * (0.4 + culture.Values.Aggression);
        if (!rng.Chance(chance)) return;

        settlement.IsFortified = true;

        world.Chronicle.Record(
            year,
            EventKind.SettlementFortified,
            settlement.Id,
            obj: settlement.CivilizationId,
            location: settlement.RegionId);
    }

    private static void Abandon(WorldState world, Settlement settlement, int year)
    {
        settlement.AbandonedYear = year;

        Region region = world.Regions[settlement.RegionId];
        if (region.Owner == settlement.CivilizationId)
        {
            region.Owner = EntityId.None;
            world.Civilizations[settlement.CivilizationId].TerritoryRegionIds.Remove(region.Id);
        }

        world.Chronicle.Record(
            year,
            EventKind.SettlementAbandoned,
            settlement.Id,
            obj: settlement.CivilizationId,
            location: settlement.RegionId,
            data: Chronicle.Data(
                ("years", (year - settlement.FoundedYear).ToString(CultureInfo.InvariantCulture)),
                ("peakPopulation", settlement.PeakPopulation.ToString(CultureInfo.InvariantCulture))));
    }

    private static void Fall(WorldState world, Civilization civilization, int year)
    {
        civilization.EndedYear = year;
        civilization.Population = 0;

        // A ruler without a realm stops ruling, but does not die of it.
        if (!civilization.CurrentRulerId.IsNone)
        {
            Figure ruler = world.Figures[civilization.CurrentRulerId];
            if (ruler.IsAlive) ruler.EndCurrentTitle(year);
            civilization.CurrentRulerId = EntityId.None;
        }

        world.Chronicle.Record(
            year,
            EventKind.CivilizationFell,
            civilization.Id,
            data: Chronicle.Data(
                ("years", (year - civilization.FoundedYear).ToString(CultureInfo.InvariantCulture)),
                ("peakPopulation", civilization.PeakPopulation.ToString(CultureInfo.InvariantCulture))));
    }
}
