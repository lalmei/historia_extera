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
public sealed class PopulationSystem : ISystem
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
    /// <para>Applied to <em>squared</em> fertility, not fertility. A linear term gives marginal land
    /// far too much: at a flat 9,000 per unit, ground scoring 0.05 still fed four hundred people, so
    /// no settlement anywhere could ever fail and abandonment was unreachable in practice. Squaring
    /// makes poor land disproportionately poor, which is both closer to the truth and what opens
    /// the dynamic range decline needs.</para>
    ///
    /// <para><b>Larger than it looks, because the land is now shared.</b> This is the capacity of a
    /// whole neighbourhood's fields, not of one settlement's, and <see cref="Hinterland"/> divides
    /// it among everybody close enough to work it — an ordinary settlement keeps something like a
    /// third to a half. It was 9,000 when each settlement drew on its own region as though nobody
    /// else were there, and raising it alongside the division is what keeps a world's total living
    /// population in the same range it always had while changing how that population is
    /// distributed. Squared fertility across the regions people actually settle spans a factor of
    /// thirty-seven from the tenth percentile to the ninetieth, so this term supplies the
    /// hierarchy's spread; competition for it supplies the hierarchy's shape.</para>
    ///
    /// <para>Calibrated by sweeping reach against this number over thousand-year runs on five
    /// seeds, against three things at once: a world that stays populated, a median settlement that
    /// is not a town, and a size distribution with a tail rather than a hump.</para>
    /// </remarks>
    private const double CapacityFromFertility = 26000.0;
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

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        IRng rng = world.Root.Fork(Name, year);

        // Once for the world, not once per settlement: TradeRoutes.From walks every route in the
        // table, and asking it per settlement per year is a quadratic no chronicle needs.
        TradeTraffic traffic = TradeRoutes.TrafficBySettlement(world);

        // Taken before anybody grows. Hinterland shares must be decided against one consistent
        // picture of the world, or a settlement's share would depend on whether its neighbours
        // happened to be walked before or after it — which is civilization id order, and would
        // quietly hand the world's first realm the best land.
        Hinterland hinterland = Hinterland.Survey(world);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);
            int total = 0;

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                Region region = world.Regions[settlement.RegionId];
                double harvest = world.Harvest.QualityAt(region, year);

                double capacity = CapacityOf(
                    world,
                    civilization,
                    culture,
                    settlement,
                    region,
                    harvest,
                    traffic.At(settlement.Id),
                    hinterland.ShareFor(world, settlement));

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

                // The peak follows the write itself — see Settlement.Population — so it is already
                // current here and the depression test below reads this year's figure.

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
    /// <remarks>
    /// <para><paramref name="routeTraffic"/> is the summed traffic of every live trade route
    /// touching the settlement. Callers outside the yearly tick can get it from
    /// <see cref="TradeRoutes.TrafficAt"/>; the tick itself builds the whole table once a year
    /// instead, because the per-settlement query walks the route table.</para>
    ///
    /// <para><paramref name="landShare"/> is the fraction of the surrounding country this
    /// settlement feeds itself from rather than a neighbour — see <see cref="Hinterland"/>. One
    /// means nothing else is near enough to compete.</para>
    /// </remarks>
    public static double CapacityOf(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Settlement settlement,
        Region region,
        double harvest,
        double routeTraffic,
        double landShare) =>
        SupportFor(world, civilization, culture, settlement, region, harvest, routeTraffic, landShare)
            .Capacity;

    /// <summary>
    /// The same calculation as <see cref="CapacityOf"/>, itemised.
    /// </summary>
    /// <remarks>
    /// <para>Exists so the export can say <em>why</em> a settlement is the size it is, rather than
    /// only how large it is. A reader looking at a town of four thousand has no way to tell from
    /// the number whether it stands on exceptional ground, on six trade routes, or on a capital's
    /// administration — and those are different histories that the chronicle otherwise never
    /// distinguishes.</para>
    ///
    /// <para>The itemised parts are reported after the modifiers, so they sum to the capacity and a
    /// reader can compare them directly.</para>
    /// </remarks>
    public static SettlementSupport SupportFor(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Settlement settlement,
        Region region,
        double harvest,
        double routeTraffic,
        double landShare)
    {
        SettlementSpecialization specialization = settlement.Specialization;

        double fromLand = DetMath.IntPow(region.Fertility, 2)
                          * CapacityFromFertility
                          * Specializations.FertilityWeight(specialization)
                          * landShare;

        // What the roads bring in. A settlement no route reaches gets nothing here and lives on its
        // fields alone, which is what keeps the great majority of settlements villages.
        double fromTrade = routeTraffic * Specializations.ImportReliance(specialization);

        double fromSite = Specializations.SiteCapacity(specialization);

        // A poor year bites in proportion to how exposed the settlement's trade is to it.
        double sensitivity = Specializations.HarvestSensitivity(specialization);
        double modifier = DetMath.Lerp(1.0, DetMath.Lerp(0.18, 1.22, harvest), sensitivity);

        // Overextension. A realm can only feed and defend so far from its seat, and how far
        // depends on what the settlement needs from it.
        modifier *= SupplyFactor(world, civilization, settlement, specialization);

        // Culture. Mercantile realms sustain larger towns everywhere; pious ones sustain their
        // shrines beyond what the land would bear.
        modifier *= DetMath.Lerp(0.92, 1.14, culture.Values.Mercantile);
        if (specialization == SettlementSpecialization.Shrine)
        {
            modifier *= DetMath.Lerp(0.85, 1.45, culture.Values.Piety);
        }

        if (settlement.IsCapital) modifier *= CapitalCapacityBonus;
        if (settlement.IsFortified) modifier *= FortificationBonus;

        return new SettlementSupport(
            FromSite: fromSite * modifier,
            FromLand: fromLand * modifier,
            FromTrade: fromTrade * modifier,
            LandShare: landShare,
            RouteTraffic: routeTraffic);
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
