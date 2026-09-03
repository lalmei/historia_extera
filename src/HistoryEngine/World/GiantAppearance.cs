using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>What a ring is made of, which is what sets how bright and how coloured it reads.</summary>
public enum RingComposition
{
    /// <summary>Fresh water ice: the brightest rings, and the ones that lift a giant's magnitude.</summary>
    Ice = 0,

    /// <summary>Rock and dust ground off shepherd moonlets: dim, ruddy, easy to miss.</summary>
    RockAndDust = 1,

    /// <summary>Carbon-dark debris, darker than the planet it circles.</summary>
    Soot = 2,
}

/// <summary>
/// A ring system, measured in radii of the planet it circles and lying in that planet's equatorial
/// plane — so the giant's obliquity is also the ring tilt, and its ascending node is the direction
/// the ring line runs.
/// </summary>
/// <param name="OpticalDepth">How solid the ring reads, from a dust haze at 0 to opaque at 1.</param>
/// <param name="DivisionRadiusPlanetRadii">
/// Where the widest gap sits, swept clear by a resonance with an inner moon. Zero when the ring has
/// no division worth drawing.
/// </param>
public sealed record PlanetRing(
    double InnerRadiusPlanetRadii,
    double OuterRadiusPlanetRadii,
    double OpticalDepth,
    double DivisionRadiusPlanetRadii,
    RingComposition Composition,
    double TintR,
    double TintG,
    double TintB)
{
    public double WidthPlanetRadii => Math.Max(0.0, OuterRadiusPlanetRadii - InnerRadiusPlanetRadii);

    public bool HasDivision => DivisionRadiusPlanetRadii > InnerRadiusPlanetRadii
                               && DivisionRadiusPlanetRadii < OuterRadiusPlanetRadii;

    public string CompositionLabel => Composition switch
    {
        RingComposition.Ice => "water ice",
        RingComposition.RockAndDust => "rock and dust",
        RingComposition.Soot => "sooted debris",
        _ => Composition.ToString(),
    };
}

/// <summary>
/// A long-lived storm parked in one of a giant's bands: an anticyclone the size of a small world,
/// held in place between two jets the way Jupiter's Great Red Spot is.
/// </summary>
public sealed record PlanetStorm(
    string Name,
    double LatitudeDeg,
    double LongitudeSpanDeg,
    double LatitudeSpanDeg,
    double AgeYears,
    double TintR,
    double TintG,
    double TintB);

/// <summary>
/// How a giant looks: which way it is tipped, the banding its rotation whips up, the storm caught
/// between two of those bands, and the ring system in its equatorial plane.
/// </summary>
/// <param name="ObliquityDeg">
/// Tilt of the equator — and so of the rings — from the orbital plane. A giant with no tilt shows
/// its rings edge-on to anyone in that plane; a tipped one opens them.
/// </param>
/// <param name="Retrograde">The giant spins, and its rings run, against the direction it orbits.</param>
/// <param name="AscendingNodeDeg">
/// Where the equator crosses the orbital plane, which is the direction the ring line runs.
/// </param>
public sealed record GiantAppearance(
    double ObliquityDeg,
    bool Retrograde,
    double RotationPeriodHours,
    double AscendingNodeDeg,
    int BandCount,
    double BandLightR,
    double BandLightG,
    double BandLightB,
    double BandDarkR,
    double BandDarkG,
    double BandDarkB,
    PlanetStorm? Storm,
    PlanetRing? Ring)
{
    public bool HasRing => Ring is not null;

    public bool HasStorm => Storm is not null;
}

/// <summary>
/// Gives a giant planet a face: the tilt it spins at, the bands that tilt whips up, a long-lived
/// storm parked between two of them, and — often — a ring system in its equatorial plane.
/// </summary>
/// <remarks>
/// <para>None of this is derived from first principles the way the star and the habitable zone are.
/// A giant's banding, its spot, and whether it kept its rings are accidents of history that no
/// habitability argument settles, so they are sampled from ranges the solar system's four giants
/// bracket: rotation between eight and twenty hours, obliquity anywhere from Jupiter's three
/// degrees to Uranus lying on its side, and rings that are common but rarely as bright as
/// Saturn's.</para>
///
/// <para>The one place this feeds back into physics is brightness. Ice rings are the most
/// reflective surfaces in a system, so an open, icy ring measurably lifts the planet's magnitude —
/// which is what decides whether a chronicler sees a wanderer at all. See
/// <see cref="RingBrightnessBoostMagnitudes"/>.</para>
/// </remarks>
public static class GiantAppearances
{
    /// <summary>Rings sitting inside this many planet radii are ground back to dust and lost.</summary>
    public const double MinRingInnerPlanetRadii = 1.10;

    /// <summary>Beyond roughly this, ring particles clump into moonlets instead of staying rings.</summary>
    public const double MaxRingOuterPlanetRadii = 4.20;

    public static GiantAppearance Sample(IRng rng, CompanionRole role, double massEarth)
    {
        ArgumentNullException.ThrowIfNull(rng);

        double obliquity = SampleObliquity(rng);
        (RgbTint light, RgbTint dark) = SampleBandColors(rng, role);
        double rotationHours = role == CompanionRole.OuterIceGiant
            ? rng.NextDouble(14.0, 20.0)
            : rng.NextDouble(8.0, 13.0);

        // Faster spinners drive more jets, and so more bands, which is what sets Jupiter apart from
        // the slower ice giants.
        int bandCount = (int)Math.Round(DetMath.Clamp(150.0 / rotationHours, 4.0, 16.0));
        bandCount += rng.NextInt(-1, 2);
        bandCount = (int)DetMath.Clamp(bandCount, 3.0, 17.0);

        return new GiantAppearance(
            obliquity,
            Retrograde: rng.Chance(0.12),
            rotationHours,
            AscendingNodeDeg: rng.NextDouble(0.0, 360.0),
            bandCount,
            light.R,
            light.G,
            light.B,
            dark.R,
            dark.G,
            dark.B,
            SampleStorm(rng, role, bandCount, light, dark),
            SampleRing(rng, role, massEarth));
    }

    /// <summary>
    /// How wide open the rings look from the observer's own orbital plane. A ring lying in the
    /// orbital plane is seen edge-on and vanishes; a tipped one opens toward one node and closes at
    /// the other, so this is the average opening over an orbit rather than a single moment.
    /// </summary>
    public static double RingOpenness(GiantAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        double tilt = DetSeries.ToRadians(appearance.ObliquityDeg);
        return DetMath.Clamp01(Math.Abs(DetSeries.Sin(tilt)) * 0.637);
    }

    /// <summary>
    /// Magnitudes a ring system adds to its planet, as a negative number. Saturn's rings roughly
    /// double its light when wide open, which is about 0.7 magnitudes; an edge-on or sooty ring
    /// adds nothing.
    /// </summary>
    public static double RingBrightnessBoostMagnitudes(GiantAppearance? appearance)
    {
        if (appearance?.Ring is not { } ring) return 0.0;

        double albedo = ring.Composition switch
        {
            RingComposition.Ice => 0.60,
            RingComposition.RockAndDust => 0.20,
            RingComposition.Soot => 0.06,
            _ => 0.20,
        };

        // Ring light against planet light: the projected ring area, dimmed by how much of it the
        // particles actually fill and by how reflective they are, over the planet's own disc.
        double area = (ring.OuterRadiusPlanetRadii * ring.OuterRadiusPlanetRadii)
                      - (ring.InnerRadiusPlanetRadii * ring.InnerRadiusPlanetRadii);
        double share = area * ring.OpticalDepth * albedo * RingOpenness(appearance);
        return -2.5 * DetSeries.Log10(1.0 + Math.Max(0.0, share));
    }

    /// <summary>Which way the ring line runs against the horizon, in radians.</summary>
    public static double RingRollRadians(GiantAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        double roll = DetSeries.ToRadians((appearance.AscendingNodeDeg % 180.0) - 90.0);
        return appearance.Retrograde ? -roll : roll;
    }

    private static double SampleObliquity(IRng rng)
    {
        double roll = rng.NextDouble();
        return roll switch
        {
            // Most giants stand nearly upright, like Jupiter.
            < 0.45 => rng.NextDouble(0.5, 12.0),

            // A giant knocked over in the last stages of accretion, like Saturn or Neptune.
            < 0.85 => rng.NextDouble(15.0, 40.0),

            // And the rare world lying on its side, like Uranus.
            _ => rng.NextDouble(60.0, 98.0),
        };
    }

    private static (RgbTint Light, RgbTint Dark) SampleBandColors(IRng rng, CompanionRole role)
    {
        if (role == CompanionRole.OuterIceGiant)
        {
            // Methane over hydrogen: the colder the haze, the further from teal toward deep blue.
            var iceLight = new RgbTint(
                rng.NextDouble(0.55, 0.74),
                rng.NextDouble(0.82, 0.94),
                rng.NextDouble(0.93, 1.00));
            var iceDark = new RgbTint(
                rng.NextDouble(0.10, 0.24),
                rng.NextDouble(0.34, 0.52),
                rng.NextDouble(0.62, 0.82));
            return (iceLight, iceDark);
        }

        // Ammonia cloud decks over sulfur and phosphorus hazes: cream zones and ruddy belts.
        double warm = rng.NextDouble();
        var light = new RgbTint(
            rng.NextDouble(0.90, 1.00),
            rng.NextDouble(0.84, 0.95),
            rng.NextDouble(0.66 + (0.14 * (1.0 - warm)), 0.86));
        var dark = new RgbTint(
            rng.NextDouble(0.52, 0.74),
            rng.NextDouble(0.30, 0.48),
            rng.NextDouble(0.16, 0.32));
        return (light, dark);
    }

    private static PlanetStorm? SampleStorm(
        IRng rng,
        CompanionRole role,
        int bandCount,
        RgbTint light,
        RgbTint dark)
    {
        double chance = role == CompanionRole.OuterIceGiant ? 0.45 : 0.80;
        if (!rng.Chance(chance)) return null;

        // Anticyclones are pinned between two jets, so a storm sits on a band boundary rather than
        // anywhere on the globe.
        int band = rng.NextInt(1, Math.Max(2, bandCount - 1));
        double latitude = ((band / (double)bandCount) - 0.5) * 2.0 * 62.0;
        if (rng.Chance(0.5)) latitude = -latitude;

        double span = rng.NextDouble(18.0, 46.0);
        RgbTint tint = role == CompanionRole.OuterIceGiant
            ? new RgbTint(
                rng.NextDouble(0.08, 0.20),
                rng.NextDouble(0.16, 0.30),
                rng.NextDouble(0.40, 0.62))
            : new RgbTint(
                rng.NextDouble(0.72, 0.95),
                rng.NextDouble(0.26, 0.52),
                rng.NextDouble(0.18, 0.36));

        // A pale storm on a dark band reads as well as a dark one on a pale band; pick whichever
        // contrasts with the deck it sits on.
        if (rng.Chance(0.25))
        {
            tint = new RgbTint(
                DetMath.Clamp01(((light.R + dark.R) * 0.5) + 0.18),
                DetMath.Clamp01(((light.G + dark.G) * 0.5) + 0.14),
                DetMath.Clamp01(((light.B + dark.B) * 0.5) + 0.10));
        }

        return new PlanetStorm(
            rng.Pick(StormNames),
            latitude,
            span,
            LatitudeSpanDeg: span * rng.NextDouble(0.35, 0.60),
            AgeYears: rng.NextDouble(80.0, 4200.0),
            tint.R,
            tint.G,
            tint.B);
    }

    private static PlanetRing? SampleRing(IRng rng, CompanionRole role, double massEarth)
    {
        // Every giant in the solar system has rings; only one has rings worth seeing. Massive
        // giants hold theirs longest, so mass tips the odds rather than deciding them.
        double chance = role switch
        {
            CompanionRole.ShepherdGiant => 0.72,
            CompanionRole.OuterGasGiant => 0.68,
            CompanionRole.OuterIceGiant => 0.45,
            _ => 0.0,
        };

        chance += DetMath.Clamp((massEarth - 100.0) / 1200.0, -0.08, 0.12);
        if (!rng.Chance(chance)) return null;

        double inner = rng.NextDouble(MinRingInnerPlanetRadii, 1.85);
        double outer = Math.Min(inner + rng.NextDouble(0.35, 2.30), MaxRingOuterPlanetRadii);
        if (outer <= inner + 0.15) outer = inner + 0.15;

        RingComposition composition = SampleComposition(rng, role);
        double opticalDepth = composition switch
        {
            RingComposition.Ice => rng.NextDouble(0.35, 0.95),
            RingComposition.RockAndDust => rng.NextDouble(0.08, 0.40),
            _ => rng.NextDouble(0.03, 0.22),
        };

        // A moonlet in resonance sweeps one lane clear, the way Mimas keeps the Cassini division.
        double division = rng.Chance(0.55)
            ? inner + ((outer - inner) * rng.NextDouble(0.35, 0.75))
            : 0.0;

        RgbTint tint = composition switch
        {
            RingComposition.Ice => new RgbTint(
                rng.NextDouble(0.86, 1.00),
                rng.NextDouble(0.88, 1.00),
                rng.NextDouble(0.90, 1.00)),
            RingComposition.RockAndDust => new RgbTint(
                rng.NextDouble(0.72, 0.90),
                rng.NextDouble(0.58, 0.74),
                rng.NextDouble(0.42, 0.58)),
            _ => new RgbTint(
                rng.NextDouble(0.34, 0.48),
                rng.NextDouble(0.30, 0.42),
                rng.NextDouble(0.30, 0.44)),
        };

        return new PlanetRing(inner, outer, opticalDepth, division, composition, tint.R, tint.G, tint.B);
    }

    private static RingComposition SampleComposition(IRng rng, CompanionRole role)
    {
        double roll = rng.NextDouble();
        if (role == CompanionRole.OuterIceGiant)
        {
            // Far from the star, ring ice darkens under irradiation rather than staying bright.
            return roll < 0.30 ? RingComposition.Ice
                : roll < 0.70 ? RingComposition.RockAndDust
                : RingComposition.Soot;
        }

        return roll < 0.58 ? RingComposition.Ice
            : roll < 0.90 ? RingComposition.RockAndDust
            : RingComposition.Soot;
    }

    private static readonly string[] StormNames =
    {
        "the Great Eye",
        "the Long Storm",
        "the Red Wake",
        "the Amber Spot",
        "the Standing Gyre",
        "the Pale Oval",
        "the Old Wound",
        "the Slow Whorl",
    };

    /// <summary>A colour in linear 0–1 channels, used only to keep the samplers readable.</summary>
    private readonly record struct RgbTint(double R, double G, double B);
}
