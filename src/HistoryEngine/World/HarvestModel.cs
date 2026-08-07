using HistoryEngine.Core;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// How good the year was, per region.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Before it, population growth was logistic against a fixed
/// carrying capacity, which meant every settlement grew monotonically toward its ceiling and
/// stopped. Nothing ever declined, so nothing was ever abandoned, so no civilization ever fell —
/// the machinery for all three existed and could not fire. A history where everything only ever
/// gets bigger is not a history.</para>
///
/// <para><b>Correlated in space and time, deliberately.</b> Independent per-region-per-year rolls
/// would average out: every settlement would have mediocre years forever and none would have a run
/// bad enough to kill it. Real settlements are abandoned after <em>sustained regional</em> failure,
/// so the field is sampled from noise that drifts diagonally with the year — neighbouring regions
/// share a bad harvest, and a bad harvest tends to persist for several years before moving on. That
/// is what produces the multi-year regional famines that actually empty a marginal village.</para>
///
/// <para>Stateless and seeded, so a region's harvest in year 240 is the same on every run and can
/// be queried out of order without keeping history.</para>
/// </remarks>
public sealed class HarvestModel
{
    /// <summary>World units per unit of harvest noise. Larger means broader droughts.</summary>
    private const double SpatialScale = 2600.0;

    /// <summary>Years per unit of drift in the weather field. Sets how long a drought runs.</summary>
    private const double WeatherPeriod = 9.0;

    /// <summary>
    /// Years per unit of drift in the climate field.
    /// </summary>
    /// <remarks>
    /// The long timescale, and the reason it exists. With weather alone at nine years, a marginal
    /// settlement always recovered before it could die: populations shed people for a few years,
    /// the field moved on, and they grew back. Nothing was ever abandoned. Historical abandonments
    /// follow multi-decade climate shifts rather than single droughts, so a region can now sit in a
    /// poor phase for a generation — long enough to finish a village that a passing dry spell only
    /// bruises.
    /// </remarks>
    private const double ClimatePeriod = 70.0;

    /// <summary>Broader in space than weather: a climate phase covers a whole region of the map.</summary>
    private const double ClimateSpatialScale = SpatialScale * 2.2;

    private readonly ulong _weatherSeed;
    private readonly ulong _climateSeed;

    public HarvestModel(ulong worldSeed)
    {
        _weatherSeed = Hash.Combine(worldSeed, Hash.OfString("harvest.weather"));
        _climateSeed = Hash.Combine(worldSeed, Hash.OfString("harvest.climate"));
    }

    /// <summary>
    /// Harvest quality in [0, 1] for a region in a given year. 0.5 is an ordinary year.
    /// </summary>
    /// <remarks>
    /// The year drifts the sample point diagonally across the noise field, which couples space and
    /// time in one 2D lookup: a bad patch both covers neighbouring regions and lingers across
    /// neighbouring years, then moves. A third noise dimension would be cleaner in principle and
    /// buys nothing here.
    ///
    /// <para>Biased by the region's own climate, so arid and marginal land has poor years more
    /// often — the same regions that were only barely worth settling.</para>
    /// </remarks>
    public double QualityAt(Region region, int year)
    {
        // Weather: year-to-year variation, regional in extent, over in under a decade.
        double weatherDrift = year / WeatherPeriod;
        double weather = ValueNoise.Fbm(
            _weatherSeed,
            (region.CenterX / SpatialScale) + weatherDrift,
            (region.CenterZ / SpatialScale) - weatherDrift,
            octaves: 3);

        // Climate: a slow drift that can hold a whole part of the map in a poor phase for a
        // generation. This is the component a settlement cannot outlast.
        double climateDrift = year / ClimatePeriod;
        double climate = ValueNoise.Fbm(
            _climateSeed,
            (region.CenterX / ClimateSpatialScale) + climateDrift,
            (region.CenterZ / ClimateSpatialScale) - climateDrift,
            octaves: 2);

        // Noise is concentrated near zero once octaves are normalised, so a straight rescale to
        // [0, 1] leaves almost every year mediocre and none bad enough to matter. Widening past
        // the unit interval and clamping puts real mass at both extremes.
        double quality = DetMath.Clamp01(0.5 + (weather * 0.48) + (climate * 0.54));

        // Marginal land fails more often. Habitability already folds in fertility, water and
        // relief, so it is the right single handle for "how forgiving is this place".
        double forgiveness = DetMath.Lerp(0.78, 1.06, region.Habitability);

        return DetMath.Clamp01(quality * forgiveness);
    }

    /// <summary>Whether a year is poor enough to be worth recording as a famine.</summary>
    public static bool IsFamine(double quality) => quality < 0.28;

    /// <summary>A label for the severity of a poor year, for event prose.</summary>
    public static string SeverityLabel(double quality) => quality switch
    {
        < 0.12 => "a catastrophic failure of the harvest",
        < 0.20 => "a severe famine",
        _ => "a failed harvest",
    };
}
