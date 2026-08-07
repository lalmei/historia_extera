namespace HistoryEngine.Core;

/// <summary>
/// Floating-point helpers that are safe to use on a decision path.
/// </summary>
/// <remarks>
/// IEEE 754 requires <c>+ - * /</c> and <c>sqrt</c> to be correctly rounded, so those
/// are bit-identical on every conforming runtime. The transcendentals — <c>Sin</c>,
/// <c>Cos</c>, <c>Tan</c>, <c>Pow</c>, <c>Exp</c>, <c>Log</c> — carry no such guarantee
/// and are free to differ by an ULP between runtimes, platforms and even hardware
/// generations.
///
/// <para>An ULP sounds harmless until it lands next to a comparison. One
/// <c>Chance(score)</c> that falls on the other side of a threshold changes a single
/// decision, that decision changes which entity id gets allocated next, and the two
/// histories diverge completely from that year on. This engine builds on net7 and
/// tests on net10, so the risk is live rather than theoretical.</para>
///
/// <para>Nothing here needs transcendentals: the noise field uses gradient tables and
/// integer hashing, and every scoring curve below is polynomial. Enforced by
/// <c>DeterminismGuardTests</c>.</para>
/// </remarks>
public static class DetMath
{
    public static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    public static double Clamp01(double value) => Clamp(value, 0.0, 1.0);

    public static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    /// <summary>Fraction of the way <paramref name="value"/> sits between the bounds, clamped.</summary>
    public static double InverseLerp(double a, double b, double value)
    {
        if (a == b) return 0.0;
        return Clamp01((value - a) / (b - a));
    }

    /// <summary>Hermite smoothstep, 3t² − 2t³. The interpolant for the noise field.</summary>
    public static double SmoothStep(double t)
    {
        t = Clamp01(t);
        return t * t * (3.0 - (2.0 * t));
    }

    /// <summary>Quintic smootherstep, 6t⁵ − 15t⁴ + 10t³. Continuous second derivative.</summary>
    public static double SmootherStep(double t)
    {
        t = Clamp01(t);
        return t * t * t * ((t * ((t * 6.0) - 15.0)) + 10.0);
    }

    /// <summary>
    /// A unimodal response in [0, 1]: 1 at <paramref name="optimum"/>, falling to 0 at
    /// <paramref name="tolerance"/> either side.
    /// </summary>
    /// <remarks>
    /// For "how well does this value suit that requirement" scoring. The obvious alternative — a
    /// pair of clamped <see cref="Lerp"/> ramps — plateaus at 1 across the whole comfortable range,
    /// which makes a product of several such factors saturate: every temperate lowland scored a
    /// perfect 1.0 for fertility, so the model could not tell good farmland from excellent and
    /// nothing downstream of it could discriminate either. A quadratic falls away immediately from
    /// the optimum, so there is only one best value rather than a plateau of them.
    /// </remarks>
    public static double Bump(double value, double optimum, double tolerance)
    {
        if (tolerance <= 0.0) return value == optimum ? 1.0 : 0.0;

        double offset = (value - optimum) / tolerance;
        return Clamp01(1.0 - (offset * offset));
    }

    /// <summary>Correctly rounded per IEEE 754, so safe on a decision path.</summary>
    public static double Sqrt(double value) => Math.Sqrt(value);

    public static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Sqrt((dx * dx) + (dy * dy));
    }

    public static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// Integer exponentiation by squaring — a reproducible stand-in for
    /// <c>Math.Pow</c> when the exponent is a small whole number, which on scoring
    /// curves it almost always is.
    /// </summary>
    public static double IntPow(double value, int exponent)
    {
        if (exponent < 0)
        {
            return 1.0 / IntPow(value, -exponent);
        }

        double result = 1.0;
        double b = value;
        int e = exponent;

        while (e > 0)
        {
            if ((e & 1) == 1) result *= b;
            b *= b;
            e >>= 1;
        }

        return result;
    }
}
