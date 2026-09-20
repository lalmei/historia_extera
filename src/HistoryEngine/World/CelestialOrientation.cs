using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>
/// How the world's spin axis sits relative to its galaxy, which is what turns a galactic star
/// position into the right ascension and declination a sky needs.
/// </summary>
/// <remarks>
/// <para>There is no reason for a planet's pole to line up with its galaxy, so the pole is drawn
/// uniformly over the sphere. The tilt that follows says how the galactic plane sits against the
/// celestial equator, and so whether the band of light holds its altitude through the night or
/// sweeps across the sky. It is not an angle to anybody's horizon: that needs a surface observer,
/// a geographic latitude and a sidereal time, and the engine has none of the three. The engine
/// exports the orientation and exposes coordinate transforms. The viewer renders no local
/// horizon, and no historical decision reads any of this.</para>
///
/// <para>Rolled on its own stream, the way the galaxy is, so adding an orientation cannot
/// reshuffle the star or the habitable body.</para>
/// </remarks>
public sealed record CelestialOrientation(
    double PoleGalacticLongitudeRad,
    double PoleGalacticLatitudeRad,
    double RightAscensionOriginRollRad)
{
    /// <summary>Draws a pole uniformly over the sphere, plus the roll that fixes right ascension zero.</summary>
    public static CelestialOrientation From(ulong seed)
    {
        IRng rng = new Pcg32(Hash.Combine(seed, Hash.OfString("world.cosmology.orientation")));
        return Sample(rng);
    }

    public static CelestialOrientation Sample(IRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        // Uniform in sin(latitude) rather than in latitude, or poles would be over-sampled.
        double latitude = DetSeries.Asin(1.0 - (2.0 * rng.NextDouble()));
        double longitude = rng.NextDouble(-DetSeries.Pi, DetSeries.Pi);
        double roll = rng.NextDouble(0.0, DetSeries.TwoPi);
        return new CelestialOrientation(longitude, latitude, roll);
    }

    /// <summary>
    /// Angle between the celestial pole and the nearer galactic pole, so 0–90° whichever way the
    /// axis points; Earth's is about 63°. Because each plane's normal is the pole above it, this
    /// is also the inclination of the galactic plane to the celestial equator: near 0° the two
    /// planes coincide and the band of light keeps its altitude all night, near 90° the axis lies
    /// in the galactic plane and the band sweeps across the sky. Which galactic hemisphere the
    /// pole points into is the sign of <see cref="PoleGalacticLatitudeRad"/>, not this angle.
    /// </summary>
    public double PoleTiltFromGalacticPoleDeg =>
        DetSeries.ToDegrees(DetSeries.HalfPi - Math.Abs(PoleGalacticLatitudeRad));

    /// <summary>
    /// Converts a galactic direction, longitude 0 at the nucleus, into equatorial coordinates in
    /// degrees with right ascension in [0, 360).
    /// </summary>
    public (double RightAscensionDeg, double DeclinationDeg) ToEquatorial(
        double galacticLongitudeRad,
        double galacticLatitudeRad)
    {
        double cosB = DetSeries.Cos(galacticLatitudeRad);
        Vector star = new(
            cosB * DetSeries.Cos(galacticLongitudeRad),
            cosB * DetSeries.Sin(galacticLongitudeRad),
            DetSeries.Sin(galacticLatitudeRad));

        (Vector pole, Vector raOrigin, Vector third) = Basis();
        double declination = DetSeries.Asin(Dot(star, pole));
        double rightAscension = DetSeries.Atan2(Dot(star, third), Dot(star, raOrigin));
        if (rightAscension < 0.0) rightAscension += DetSeries.TwoPi;

        return (DetSeries.ToDegrees(rightAscension), DetSeries.ToDegrees(declination));
    }

    /// <summary>
    /// Inverse of <see cref="ToEquatorial"/>: an equatorial direction back into galactic longitude
    /// and latitude, longitude 0 at the nucleus.
    /// </summary>
    public (double GalacticLongitudeRad, double GalacticLatitudeRad) ToGalactic(
        double rightAscensionDeg,
        double declinationDeg)
    {
        double rightAscension = DetSeries.ToRadians(rightAscensionDeg);
        double declination = DetSeries.ToRadians(declinationDeg);
        double cosDec = DetSeries.Cos(declination);
        (Vector pole, Vector raOrigin, Vector third) = Basis();

        Vector galactic = Add(
            Add(
                Scale(raOrigin, cosDec * DetSeries.Cos(rightAscension)),
                Scale(third, cosDec * DetSeries.Sin(rightAscension))),
            Scale(pole, DetSeries.Sin(declination)));

        return (DetSeries.Atan2(galactic.Y, galactic.X), DetSeries.Asin(galactic.Z));
    }

    private (Vector Pole, Vector RaOrigin, Vector Third) Basis()
    {
        double cosB = DetSeries.Cos(PoleGalacticLatitudeRad);
        Vector pole = new(
            cosB * DetSeries.Cos(PoleGalacticLongitudeRad),
            cosB * DetSeries.Sin(PoleGalacticLongitudeRad),
            DetSeries.Sin(PoleGalacticLatitudeRad));

        // Any vector not parallel to the pole works as a seed for the equatorial plane; the roll
        // then fixes where right ascension zero falls.
        Vector seed = Math.Abs(pole.Z) < 0.9 ? new Vector(0.0, 0.0, 1.0) : new Vector(1.0, 0.0, 0.0);
        Vector reference = Normalize(Subtract(seed, Scale(pole, Dot(seed, pole))));
        Vector perpendicular = Cross(pole, reference);

        double cosRoll = DetSeries.Cos(RightAscensionOriginRollRad);
        double sinRoll = DetSeries.Sin(RightAscensionOriginRollRad);
        Vector raOrigin = Normalize(Add(Scale(reference, cosRoll), Scale(perpendicular, sinRoll)));
        return (pole, raOrigin, Cross(pole, raOrigin));
    }

    private readonly record struct Vector(double X, double Y, double Z);

    private static double Dot(Vector a, Vector b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static Vector Cross(Vector a, Vector b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));

    private static Vector Add(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static Vector Subtract(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static Vector Scale(Vector a, double factor) => new(a.X * factor, a.Y * factor, a.Z * factor);

    private static Vector Normalize(Vector a)
    {
        double length = DetMath.Sqrt(Dot(a, a));
        return length < 1e-12 ? new Vector(1.0, 0.0, 0.0) : Scale(a, 1.0 / length);
    }
}
