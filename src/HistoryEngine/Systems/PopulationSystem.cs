using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Grows and shrinks settlement populations, and rolls each civilization's total.
/// </summary>
/// <remarks>
/// Logistic growth against a per-settlement carrying capacity derived from the land: a
/// settlement on good ground climbs toward a high ceiling and levels off, one on marginal
/// ground stalls early, and one pushed past its capacity declines. That single mechanism is
/// what makes geography visible in the chronicle — cities appear where the land supports
/// them without anything being told to put them there.
///
/// <para>Land quality comes from the region's cached score, so this system samples no terrain
/// at all despite running for every settlement every year. It is the case the sample budget
/// test is really guarding: an innocent-looking <c>SampleExact(settlement.X, settlement.Z)</c>
/// here would be millions of samples over a full run.</para>
/// </remarks>
public sealed class PopulationSystem : IYearSystem
{
    /// <summary>Annual growth rate for a settlement far below its capacity.</summary>
    private const double BaseGrowthRate = 0.016;

    /// <summary>Year-to-year variation, standing in for harvests, disease and migration.</summary>
    private const double Volatility = 0.007;

    private const double CapacityFloor = 700.0;
    private const double CapacityFromFertility = 11000.0;
    private const double CapitalCapacityBonus = 1.35;

    public string Name => "population";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            int total = 0;

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                Region region = world.Regions[settlement.RegionId];
                double capacity = CapacityOf(region, settlement);

                // Logistic: approaches zero at capacity, goes negative beyond it.
                double headroom = 1.0 - (settlement.Population / capacity);
                double rate = (BaseGrowthRate * headroom) + rng.NextDouble(-Volatility, Volatility);

                settlement.Population = Grow(settlement.Population, rate, rng);

                if (settlement.Population > settlement.PeakPopulation)
                {
                    settlement.PeakPopulation = settlement.Population;
                }

                total += settlement.Population;
            }

            civilization.Population = total;
            if (total > civilization.PeakPopulation)
            {
                civilization.PeakPopulation = total;
            }
        }
    }

    private static double CapacityOf(Region region, Settlement settlement)
    {
        double capacity = CapacityFloor + (region.Fertility * CapacityFromFertility);

        // A capital draws people beyond what its land alone would support.
        if (settlement.IsCapital) capacity *= CapitalCapacityBonus;

        return capacity;
    }

    /// <summary>
    /// Applies a growth rate to an integer population.
    /// </summary>
    /// <remarks>
    /// Truncating <c>population * rate</c> would freeze any settlement small enough that its
    /// yearly change rounds to zero — a hamlet of forty people would never reach forty-one,
    /// so nothing would ever grow from nothing. The fractional remainder is instead resolved
    /// as a probability, which keeps small settlements moving while staying deterministic.
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
