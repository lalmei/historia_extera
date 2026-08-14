using HistoryEngine.Core;

namespace HistoryEngine.Terrain;

/// <summary>
/// Deterministic value noise and fractal Brownian motion.
/// </summary>
/// <remarks>
/// Lattice values come from integer hashing and are blended with a quintic polynomial,
/// so there is no trigonometry anywhere in the field — see <see cref="DetMath"/> for why
/// that is a hard requirement rather than an optimisation.
///
/// <para>Value noise rather than Perlin or simplex because it needs no gradient table,
/// is trivially reproducible from a seed and a coordinate, and this is placeholder
/// terrain that Phase 2 will replace outright. Visual quality is not the goal; being
/// obviously correct is.</para>
/// </remarks>
internal static class ValueNoise
{
    /// <summary>Hashed lattice value in [-1, 1].</summary>
    private static double At(ulong seed, int x, int z)
    {
        // Top 53 bits into [0, 1), then rescale. Powers of two, so exact.
        ulong h = Hash.OfCoord(seed, x, z);
        double unit = (h >> 11) * (1.0 / 9007199254740992.0);
        return (unit * 2.0) - 1.0;
    }

    private static int Floor(double v) => (int)Math.Floor(v);

    /// <summary>Single-octave value noise in [-1, 1].</summary>
    public static double Sample(ulong seed, double x, double z)
    {
        int x0 = Floor(x);
        int z0 = Floor(z);

        double tx = DetMath.SmootherStep(x - x0);
        double tz = DetMath.SmootherStep(z - z0);

        double v00 = At(seed, x0, z0);
        double v10 = At(seed, x0 + 1, z0);
        double v01 = At(seed, x0, z0 + 1);
        double v11 = At(seed, x0 + 1, z0 + 1);

        double top = DetMath.Lerp(v00, v10, tx);
        double bottom = DetMath.Lerp(v01, v11, tx);
        return DetMath.Lerp(top, bottom, tz);
    }

    /// <summary>Summed octaves in roughly [-1, 1].</summary>
    public static double Fbm(
        ulong seed,
        double x,
        double z,
        int octaves,
        double lacunarity = 2.0,
        double gain = 0.5)
    {
        double sum = 0.0;
        double amplitude = 1.0;
        double frequency = 1.0;
        double normalisation = 0.0;

        for (int o = 0; o < octaves; o++)
        {
            // Each octave gets its own derived seed so octaves are uncorrelated
            // without needing coordinate offsets.
            ulong octaveSeed = Hash.Combine(seed, (ulong)(o + 1));
            sum += Sample(octaveSeed, x * frequency, z * frequency) * amplitude;
            normalisation += amplitude;

            amplitude *= gain;
            frequency *= lacunarity;
        }

        return normalisation > 0.0 ? sum / normalisation : 0.0;
    }

    /// <summary>Fractal noise whose X axis repeats after <paramref name="periodX"/>.</summary>
    public static double FbmPeriodicX(
        ulong seed,
        double x,
        double z,
        double periodX,
        int octaves,
        double lacunarity = 2.0,
        double gain = 0.5) =>
        BlendPeriodicX(
            x,
            periodX,
            sampleX => Fbm(seed, sampleX, z, octaves, lacunarity, gain));

    /// <summary>
    /// Ridged multifractal in [0, 1], for mountain chains.
    /// </summary>
    /// <remarks>Folding |noise| creates creases; inverting turns the creases into ridges.</remarks>
    public static double Ridged(
        ulong seed,
        double x,
        double z,
        int octaves,
        double lacunarity = 2.0,
        double gain = 0.5)
    {
        double sum = 0.0;
        double amplitude = 1.0;
        double frequency = 1.0;
        double normalisation = 0.0;

        for (int o = 0; o < octaves; o++)
        {
            ulong octaveSeed = Hash.Combine(seed, (ulong)(o + 101));
            double n = Sample(octaveSeed, x * frequency, z * frequency);
            double ridge = 1.0 - Math.Abs(n);
            sum += ridge * ridge * amplitude;
            normalisation += amplitude;

            amplitude *= gain;
            frequency *= lacunarity;
        }

        return normalisation > 0.0 ? DetMath.Clamp01(sum / normalisation) : 0.0;
    }

    /// <summary>Ridged fractal noise whose X axis repeats after <paramref name="periodX"/>.</summary>
    public static double RidgedPeriodicX(
        ulong seed,
        double x,
        double z,
        double periodX,
        int octaves,
        double lacunarity = 2.0,
        double gain = 0.5) =>
        BlendPeriodicX(
            x,
            periodX,
            sampleX => Ridged(seed, sampleX, z, octaves, lacunarity, gain));

    /// <summary>
    /// Cross-fades a field with a copy one period to its west. Smootherstep pins the blend's
    /// first two derivatives at both ends, so the repeated seam has no value or slope kink.
    /// </summary>
    private static double BlendPeriodicX(double x, double periodX, Func<double, double> sample)
    {
        if (periodX <= 0.0) return sample(x);

        double wrapped = x % periodX;
        if (wrapped < 0.0) wrapped += periodX;

        double blend = DetMath.SmootherStep(wrapped / periodX);
        return DetMath.Lerp(sample(wrapped), sample(wrapped - periodX), blend);
    }
}
