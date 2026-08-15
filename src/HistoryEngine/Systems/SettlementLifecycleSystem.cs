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
    /// <summary>Population at or below which a settlement is given up outright.</summary>
    private const int AbandonmentThreshold = 12;

    /// <summary>
    /// Fraction of its own peak below which a long-declining settlement is finished.
    /// </summary>
    /// <remarks>
    /// Relative to the settlement's own history rather than an absolute headcount. An absolute
    /// floor has to be calibrated against carrying capacity, and gets it wrong at both ends: too
    /// low and nothing is ever abandoned because the logistic curve flattens before reaching it,
    /// too high and thriving small villages are condemned. "Has lost two thirds of the people it
    /// once had, and is still losing them" is the condition that actually describes a dying place.
    /// </remarks>
    private const double FatalDeclineFraction = 0.45;

    /// <summary>
    /// Peak population at which a settlement endures decline for <see cref="LargePlacePatience"/>
    /// times as long before it is given up.
    /// </summary>
    /// <remarks>
    /// <para>This replaces an absolute ceiling of four hundred people, below which a settlement had
    /// to fall before a long decline could finish it. The intent was right — a large town is not
    /// abandoned quietly — but an absolute floor beside a relative test reintroduces exactly the
    /// failure the relative test was written to avoid, and it did: anything that had ever grown
    /// past about nine hundred people could no longer be abandoned at all, because the relative
    /// test fires at 45% of peak and the ceiling demanded far less than that.</para>
    ///
    /// <para>The effect was total rather than marginal. Over a five-century run, twenty-one
    /// settlements met the decline test — several having lost two thirds of their people and stayed
    /// that way for a hundred and forty years — and every one of the twenty-one was held back by
    /// this constant alone. Across a thousand-year run of four hundred and thirty-three
    /// settlements, nothing was ever abandoned. Plague, famine, disaster and war all reduced
    /// populations that the lifecycle could then never finish, so the one system that removes
    /// settlements could not reach any of them.</para>
    ///
    /// <para>Scaling the tolerance keeps the intent and drops the veto: a city takes two or three
    /// generations of sustained decline to give up where a hamlet takes fifteen years, and both can
    /// still die. Measured against what it once was rather than what it is now, since a dying
    /// city's current population is on its way through every tier below it.</para>
    /// </remarks>
    private const double LargePlacePeak = 6000.0;

    /// <summary>How much longer the largest settlements hold on. See <see cref="LargePlacePeak"/>.</summary>
    private const double LargePlacePatience = 3.0;

    /// <summary>
    /// Years of depression after which a shrunken settlement is given up.
    /// </summary>
    /// <remarks>
    /// Fifteen years, not the twenty-five a full generation would suggest. Marginal settlements
    /// historically emptied after one or two bad decades rather than after thirty years of
    /// stubbornness, and at 25 the condition was so rare that a three-century chronicle contained no
    /// abandonment at all — the feature existed but never appeared. Tradition still stretches it
    /// toward a generation for peoples attached to their ancestral sites.
    /// </remarks>
    private const int FatalDeclineYears = 15;

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
                string? fate = FateOf(settlement, culture);
                if (fate is not null)
                {
                    Abandon(world, settlement, year, fate);
                    continue;
                }

                survivingSettlements++;
                ApplyTierChange(world, settlement, year);
                MaybeFortify(world, settlement, culture, year, rng);
            }

            if (survivingSettlements == 0)
            {
                // Shared with the war systems, so a realm that starves and one that is conquered
                // leave the world in the same state. See Realms.Fall.
                Realms.Fall(world, civilization, year);
            }
        }
    }

    /// <summary>
    /// Whether a settlement is finished, and why. Null means it survives.
    /// </summary>
    /// <remarks>
    /// Tradition stretches the tolerance for a long decline: a people attached to its ancestral
    /// sites holds on to a dying town well past the point a pragmatic one would walk away. It
    /// cannot save a settlement that has actually emptied.
    ///
    /// <para>This is the load-bearing use of <see cref="CultureValues.Tradition"/> — before
    /// Milestone 4 the trait was exported and read by nothing.</para>
    /// </remarks>
    private static string? FateOf(Settlement settlement, Culture culture)
    {
        if (settlement.Population <= AbandonmentThreshold) return "hardship";

        // 1.0 at no tradition, 1.8 at full — worth roughly another fifteen years of clinging on.
        double patience = DetMath.Lerp(1.0, 1.8, culture.Values.Tradition);

        // And a great city outlasts a hamlet by two or three generations more.
        double weight = DetMath.Lerp(
            1.0,
            LargePlacePatience,
            DetMath.InverseLerp(0.0, LargePlacePeak, settlement.PeakPopulation));

        int tolerance = (int)(FatalDeclineYears * patience * weight);

        bool shrunken = settlement.Population < settlement.PeakPopulation * FatalDeclineFraction;

        if (shrunken && settlement.YearsDepressed >= tolerance)
        {
            return "years of decline";
        }

        return null;
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

    private static void Abandon(WorldState world, Settlement settlement, int year, string cause)
    {
        settlement.AbandonedYear = year;

        Region region = world.Regions[settlement.RegionId];
        bool released = region.Owner == settlement.CivilizationId;

        if (released)
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
                ("cause", cause),
                ("peakPopulation", settlement.PeakPopulation.ToString(CultureInfo.InvariantCulture))));

        // The claim leaves with the people. Recorded rather than left implicit because a border
        // that recedes is a border change like any other, and the map replays these.
        if (released)
        {
            world.Chronicle.Record(
                year, EventKind.RegionReleased, region.Id, obj: settlement.CivilizationId);
        }

        // Whatever the place was keeping stays in it. An artifact does not walk out with the last
        // family to leave — that is precisely what makes a ruin worth digging up later.
        Treasures.LoseAll(world, settlement, year, "abandoned with the town");

        // A faith loses a congregation. Left to the religion system to pronounce dead, which it
        // does when the last one goes.
        if (!settlement.ReligionId.IsNone && world.Religions.Contains(settlement.ReligionId))
        {
            world.Religions[settlement.ReligionId].Lose(settlement.Id);
        }
    }

}
