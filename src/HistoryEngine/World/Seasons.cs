using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>
/// What time of year it is on a particular piece of ground.
/// </summary>
/// <remarks>
/// <para><b>A season is local, not global, and that is the whole reason this type exists.</b> The
/// world clock says which quarter of the year a step belongs to and nothing more. Whether that
/// quarter is a campaigning season is a property of the ground being fought over: winter in the far
/// north is high summer in the far south, and a realm on the equator has no closed season at all.
/// A global "it is winter now" would give every realm the same campaigning calendar and make the
/// clock a decoration.</para>
///
/// <para>The engine already has what this needs. A region carries its own mean
/// <see cref="Region.Temperature"/>, and its position north or south of the equator falls out of
/// <see cref="Region.CenterZ"/> against the world's size — so the seasonal swing is derived rather
/// than stored, and no terrain backend has to supply a new plane for it.</para>
///
/// <para><b>A triangle rather than a sine.</b> The shape wanted is a smooth rise to midsummer and
/// fall back, and every closed form of that needs a transcendental on what is now a decision path.
/// A triangle is within a couple of degrees of the sine across the year, and the difference is far
/// smaller than the uncertainty in the amplitude it is being multiplied by.</para>
/// </remarks>
public static class Seasons
{
    /// <summary>
    /// How far the warmest and coldest seasons sit from a region's mean, at the poles.
    /// </summary>
    /// <remarks>
    /// The equator swings by nothing and high latitudes swing hardest, which is the asymmetry that
    /// makes a closed season a northern problem. Sixteen degrees either side of the mean is a
    /// continental temperate range — enough to close a northern winter to an army without closing
    /// a Mediterranean one.
    /// </remarks>
    private const double PolarSwing = 16.0;

    /// <summary>
    /// The seasonal temperature of a region, in the same units as <see cref="Region.Temperature"/>.
    /// </summary>
    public static double Warmth(Region region, int season, int seasonsPerYear, int worldSize)
    {
        if (seasonsPerYear <= 0 || worldSize <= 0) return region.Temperature;

        // 0 at the equator, 1 at either pole.
        double fromEquator = DetMath.Clamp01(
            Math.Abs(((double)region.CenterZ / worldSize) - 0.5) * 2.0);

        // Season 0 opens the year at midwinter in the north, so the wave runs -1, 0, +1, 0 across
        // four seasons. South of the equator the same step is midsummer, hence the sign.
        double phase = (double)season / seasonsPerYear;
        double wave = 1.0 - (4.0 * Math.Abs(phase - 0.5));
        double hemisphere = region.CenterZ < worldSize / 2 ? 1.0 : -1.0;

        return region.Temperature + (PolarSwing * fromEquator * wave * hemisphere);
    }

    /// <summary>
    /// Below which an army does not take the field on this ground.
    /// </summary>
    /// <remarks>
    /// <para>Not a claim that fighting in the cold is impossible — armies have always managed it —
    /// but that a campaign is not <em>opened</em> in it. The number is deliberately low: it should
    /// close a northern winter and leave everything temperate and warmer open all year, so that the
    /// closed season is a fact about a few realms rather than a tax on all of them.</para>
    ///
    /// <para><b>Whoever else comes to ask this question should share the threshold rather than
    /// bring their own.</b> A settling party wants the same fact about the same ground — whether it
    /// can be crossed and camped on — and two numbers would drift apart without either ever being
    /// wrong enough to notice. Expansion is the obvious next caller, and is not one yet: making it
    /// seasonal measurably changed the settlement size distribution over long runs, which wants a
    /// calibration pass of its own rather than a rate divided by four.</para>
    /// </remarks>
    public const double CampaignFloor = 2.0;

    /// <summary>Whether an army would take the field on this ground in this season.</summary>
    public static bool Campaigning(Region region, int season, int seasonsPerYear, int worldSize) =>
        Warmth(region, season, seasonsPerYear, worldSize) >= CampaignFloor;

    /// <summary>
    /// How many of the year's seasons this ground is open in. Zero for what never thaws.
    /// </summary>
    /// <remarks>
    /// What lets a decision keep its yearly weight while only being taken in the seasons it can be.
    /// A frontier open twice a year is offered half as many chances at twice the odds, so its
    /// realm colonises as often as a tropical one and simply does it in summer — see
    /// <c>ExpansionSystem</c> for why that is the right shape there and the wrong one for a war.
    /// </remarks>
    public static int OpenSeasons(Region region, int seasonsPerYear, int worldSize)
    {
        int open = 0;

        for (int season = 0; season < seasonsPerYear; season++)
        {
            if (Campaigning(region, season, seasonsPerYear, worldSize)) open++;
        }

        return open;
    }
}
