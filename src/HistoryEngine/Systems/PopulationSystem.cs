using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Grows and shrinks settlement populations against a carrying capacity that moves.
/// </summary>
/// <remarks>
/// <para><b>What changed in Milestone 4.</b> Capacity used to be a fixed function of regional
/// fertility, so headroom was always positive and every settlement grew monotonically to its
/// ceiling and stopped. Nothing declined, nothing was abandoned, no civilization fell. Capacity now
/// moves year to year, and can fall below the population living against it — which is what makes
/// the rest of the lifecycle reachable at all.</para>
///
/// <para><b>Four things set capacity:</b> the land (regional fertility), what the settlement does
/// (<see cref="Specializations"/>), how the year went (<see cref="HarvestModel"/>), and how far it
/// sits from its seat of government. Each contributes differently per specialization, so a bad
/// regional decade empties the farming villages and leaves the mining town standing.</para>
///
/// <para>Still samples no terrain, despite running for every settlement every year — everything it
/// reads is a cached region statistic or a noise lookup. That is the pattern
/// <c>TerrainDisciplineTests</c> exists to protect.</para>
/// </remarks>
public sealed class PopulationSystem : IYearSystem
{
    /// <summary>
    /// Annual growth rate for a settlement far below its capacity.
    /// </summary>
    /// <remarks>
    /// <para>Calibrated so settlements reach their ceiling well inside a chronicle rather than at
    /// the end of one. At 1.6% a settlement founded with seventy people needed 269 years to approach
    /// a capacity of five thousand — so for almost the whole run every settlement sat far below its
    /// ceiling with positive headroom, and a failed harvest merely slowed growth instead of costing
    /// anyone their home. That single number was why decline, abandonment and collapse were all
    /// unreachable no matter how the climate was tuned.</para>
    ///
    /// <para>At 3.8% the same settlement reaches its ceiling in roughly 110 years and then lives at
    /// the mercy of the harvest for the remaining two centuries, which is the regime the whole
    /// lifecycle is designed around.</para>
    /// </remarks>
    private const double BaseGrowthRate = 0.038;

    /// <summary>Year-to-year variation beyond the harvest, standing in for local accident.</summary>
    private const double Volatility = 0.005;

    /// <summary>
    /// How sharply a population above capacity falls.
    /// </summary>
    /// <remarks>
    /// <para>Much larger than the growth rate on purpose: settlements empty far faster than they
    /// fill. People leave a failing village within a season and take generations to arrive at a
    /// promising one.</para>
    ///
    /// <para>Calibrated against how long a bad patch lasts. At 2.4 a settlement shed about 4% a
    /// year, so losing most of its people took some 27 years — longer than
    /// <see cref="HarvestModel"/> sustains a regional failure, which meant populations always
    /// recovered before they could die and abandonment never fired. At 5.0 a sustained failure
    /// empties a village inside its own duration.</para>
    /// </remarks>
    private const double DeclineSharpness = 5.0;

    /// <summary>
    /// Scales the fertility-derived component of capacity.
    /// </summary>
    /// <remarks>
    /// Applied to <em>squared</em> fertility, not fertility. A linear term gives marginal land far
    /// too much: at a flat 9,000 per unit, ground scoring 0.05 still fed four hundred people, so
    /// no settlement anywhere could ever fail and abandonment was unreachable in practice. Squaring
    /// makes poor land disproportionately poor, which is both closer to the truth and what opens
    /// the dynamic range decline needs. Calibrated against squared fertility: the best land reaches
    /// a large city, ordinary land a town, marginal land a village that a bad decade can empty.
    /// </remarks>
    private const double CapacityFromFertility = 9000.0;
    private const double CapitalCapacityBonus = 1.4;
    private const double FortificationBonus = 1.12;

    /// <summary>Distance from the capital, in world units, at which the supply penalty is full.</summary>
    private const double SupplyRange = 2400.0;

    /// <summary>
    /// Population loss in one year that counts as notable enough to record.
    /// </summary>
    /// <remarks>
    /// A real bad year costs a settlement a percent or two, not a sixth. Set at 6% initially, this
    /// gate required a population more than two and a half times its own carrying capacity before a
    /// famine would be written down — so no famine ever was. At the other extreme, 1.5% made famine
    /// a third of every event in the chronicle.
    /// </remarks>
    private const double NotableLossFraction = 0.03;

    /// <summary>Fraction of its own peak below which a settlement counts as depressed.</summary>
    private const double DepressedFraction = 0.5;

    public string Name => "population";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);
            int total = 0;

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                Region region = world.Regions[settlement.RegionId];
                double harvest = world.Harvest.QualityAt(region, year);

                double capacity = CapacityOf(world, civilization, culture, settlement, region, harvest);

                // Logistic: approaches zero at capacity, negative beyond it, and steeper on the
                // way down than up.
                double headroom = 1.0 - (settlement.Population / capacity);
                double rate = headroom >= 0.0
                    ? BaseGrowthRate * headroom
                    : BaseGrowthRate * headroom * DeclineSharpness;

                rate += rng.NextDouble(-Volatility, Volatility);

                int before = settlement.Population;
                settlement.Population = Grow(before, rate, rng);

                Record(world, settlement, region, before, harvest, year);

                if (settlement.Population > settlement.PeakPopulation)
                {
                    settlement.PeakPopulation = settlement.Population;
                }

                // Depression, not decline. See Settlement.YearsDepressed for why counting
                // declining years cannot work when collapse and recovery are this asymmetric.
                settlement.YearsDepressed =
                    settlement.Population < settlement.PeakPopulation * DepressedFraction
                        ? settlement.YearsDepressed + 1
                        : 0;

                total += settlement.Population;
            }

            civilization.Population = total;
            if (total > civilization.PeakPopulation)
            {
                civilization.PeakPopulation = total;
            }
        }
    }

    /// <summary>
    /// How many people this settlement can support this year.
    /// </summary>
    public static double CapacityOf(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Settlement settlement,
        Region region,
        double harvest)
    {
        SettlementSpecialization specialization = settlement.Specialization;

        double fromLand = DetMath.IntPow(region.Fertility, 2)
                          * CapacityFromFertility
                          * Specializations.FertilityWeight(specialization);

        double capacity = Specializations.BaseCapacity(specialization) + fromLand;

        // A poor year bites in proportion to how exposed the settlement's trade is to it.
        double sensitivity = Specializations.HarvestSensitivity(specialization);
        double harvestFactor = DetMath.Lerp(1.0, DetMath.Lerp(0.18, 1.22, harvest), sensitivity);
        capacity *= harvestFactor;

        // Overextension. A realm can only feed and defend so far from its seat, and how far
        // depends on what the settlement needs from it.
        capacity *= SupplyFactor(world, civilization, settlement, specialization);

        // Culture. Mercantile realms sustain larger towns everywhere; pious ones sustain their
        // shrines beyond what the land would bear.
        capacity *= DetMath.Lerp(0.92, 1.14, culture.Values.Mercantile);
        if (specialization == SettlementSpecialization.Shrine)
        {
            capacity *= DetMath.Lerp(0.85, 1.45, culture.Values.Piety);
        }

        if (settlement.IsCapital) capacity *= CapitalCapacityBonus;
        if (settlement.IsFortified) capacity *= FortificationBonus;

        // Never zero: a positive floor keeps the logistic term finite, and the abandonment
        // threshold in SettlementLifecycleSystem is what actually ends a settlement.
        return Math.Max(40.0, capacity);
    }

    private static double SupplyFactor(
        WorldState world,
        Civilization civilization,
        Settlement settlement,
        SettlementSpecialization specialization)
    {
        if (settlement.IsCapital || civilization.CapitalId.IsNone) return 1.0;
        if (!world.Settlements.Contains(civilization.CapitalId)) return 1.0;

        Settlement seat = world.Settlements[civilization.CapitalId];

        double distance = world.Distance(settlement.X, settlement.Z, seat.X, seat.Z);
        double reach = DetMath.InverseLerp(0.0, SupplyRange, distance);

        // At full distance a supply-dependent settlement keeps 45% of its capacity; an
        // independent one barely notices.
        double dependence = Specializations.SupplyDependence(specialization);
        return DetMath.Lerp(1.0, DetMath.Lerp(1.0, 0.45, reach), dependence);
    }

    /// <summary>
    /// Records a famine when a poor year cost a settlement real population.
    /// </summary>
    /// <remarks>
    /// Gated on both a poor harvest and an actual notable loss, so the chronicle records famines
    /// that mattered rather than every mediocre year. Without the second condition a drought over
    /// a mining town — which barely feels it — would still be written up as a famine.
    /// </remarks>
    private static void Record(
        WorldState world, Settlement settlement, Region region, int before, double harvest, int year)
    {
        if (!HarvestModel.IsFamine(harvest)) return;
        if (before <= 0) return;

        // A hamlet having a bad year is not chronicle-worthy. Recording it made famine the single
        // most common event in the log and buried everything else in the timeline.
        if (settlement.Tier < SettlementTier.Village) return;

        int lost = before - settlement.Population;
        if (lost <= 0 || lost < before * NotableLossFraction) return;

        // Only a famine the chronicle thought worth recording reaches the realm's fortunes. A
        // mediocre harvest that nobody wrote down is not something a court remembers a decade on.
        Realms.Suffered(world, settlement, lost);

        world.Chronicle.Record(
            year,
            EventKind.SettlementFamine,
            settlement.Id,
            obj: settlement.CivilizationId,
            location: region.Id,
            data: Chronicle.Data(
                ("severity", HarvestModel.SeverityLabel(harvest)),
                ("lost", lost.ToString(CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// Applies a growth rate to an integer population.
    /// </summary>
    /// <remarks>
    /// Truncating <c>population * rate</c> would freeze any settlement small enough that its
    /// yearly change rounds to zero — a hamlet of forty people would never reach forty-one, so
    /// nothing would ever grow from nothing. The fractional remainder is instead resolved as a
    /// probability, which keeps small settlements moving while staying deterministic.
    /// </remarks>
    private static int Grow(int population, double rate, IRng rng)
    {
        double exact = population * rate;
        int whole = (int)exact;
        double fraction = exact - whole;

        int delta = whole;
        if (fraction > 0.0 && rng.Chance(fraction)) delta++;
        else if (fraction < 0.0 && rng.Chance(-fraction)) delta--;

        return Math.Max(0, population + delta);
    }
}
