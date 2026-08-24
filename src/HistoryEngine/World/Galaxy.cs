using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>Hubble type of the host galaxy. Spirals are the common case; giant ellipticals are rare.</summary>
public enum GalaxyMorphology
{
    UnbarredSpiral = 0,
    BarredSpiral = 1,
    Elliptical = 2,
}

/// <summary>
/// Structural parameters of the host galaxy, rolled from the seed before history begins.
/// </summary>
public sealed record GalaxyBlueprint(
    GalaxyMorphology Morphology,
    double StellarMassSolar,
    double DiskScaleLengthKpc,
    double ThinDiskScaleHeightPc,
    double BulgeToDiskMass,
    double SolarAnalogMetallicityFeH,
    double MetallicityGradientDexPerKpc,
    double MetallicityScatterDex,
    int SpiralArmCount,
    double SpiralPitchDeg,
    double InnerHabitableRadiusKpc,
    double OuterHabitableRadiusKpc,
    double SersicIndex,
    double AxisRatio,
    double MetallicityReferenceRadiusKpc)
{
    public bool IsElliptical => Morphology == GalaxyMorphology.Elliptical;

    public string MorphologyLabel => Morphology switch
    {
        GalaxyMorphology.BarredSpiral => "barred spiral",
        GalaxyMorphology.UnbarredSpiral => "unbarred spiral",
        GalaxyMorphology.Elliptical => "elliptical",
        _ => Morphology.ToString(),
    };
}

/// <summary>Where the history world sits inside its host galaxy.</summary>
public sealed record GalacticLocation(
    double GalactocentricRadiusKpc,
    double AzimuthRad,
    double HeightPc,
    double MetallicityFeH,
    bool InSpiralArm,
    double LocalStellarDensityRelativeToSolar,
    double SupernovaRateRelativeToSolar);

/// <summary>
/// The seed's host galaxy and the site of the history world inside it.
/// </summary>
/// <remarks>
/// <para>Rolled from an independent stream so adding a galaxy cannot reshuffle the host star
/// or the habitable body. Flavour — it feeds no simulation decision — the way the world's
/// name and the local system already are.</para>
///
/// <para>Spirals are the common case: a metal-rich annulus outside the crowded inner disk,
/// with enough iron for a terrestrial crust. Giant ellipticals are rare; their cores are
/// dynamically hostile, so the habitable shell lives farther out in a spheroid rather than
/// a thin disk.</para>
/// </remarks>
public sealed record HostGalaxy(GalaxyBlueprint Blueprint, GalacticLocation Location)
{
    public const int MaxLocationAttempts = 256;
    public const double EllipticalChance = 0.025;
    public const double IronCoreMinimumFeH = -0.50;
    public const double OreFormingMinimumFeH = -0.30;
    public const double SolarNeighborhoodRadiusKpc = 8.0;
    public const double MaximumSafeSupernovaRate = 2.5;
    public const double SpiralArmHalfWidthRad = 0.22;

    public bool IsElliptical => Blueprint.IsElliptical;

    public bool CanHostIronCore => Location.MetallicityFeH >= IronCoreMinimumFeH;

    public bool CanHostOres => Location.MetallicityFeH >= OreFormingMinimumFeH;

    /// <summary>Builds a habitable galactic site from the seed alone, independent of the local system.</summary>
    public static HostGalaxy From(ulong seed)
    {
        IRng morphologyRng = new Pcg32(Hash.Combine(seed, Hash.OfString("world.galaxy.morphology")));
        if (morphologyRng.Chance(EllipticalChance))
        {
            IRng ellipticalRng = new Pcg32(Hash.Combine(seed, Hash.OfString("world.galaxy.elliptical")));
            return Place(GenerateElliptical(ellipticalRng), ellipticalRng);
        }

        IRng spiralRng = new Pcg32(Hash.Combine(seed, Hash.OfString("world.galaxy.spiral")));
        return Place(GenerateSpiral(spiralRng), spiralRng);
    }

    public static bool IsHabitable(GalaxyBlueprint galaxy, GalacticLocation location)
    {
        double structural = StructuralRadiusKpc(galaxy, location);
        if (structural < galaxy.InnerHabitableRadiusKpc) return false;
        if (structural > galaxy.OuterHabitableRadiusKpc) return false;
        if (!galaxy.IsElliptical && Math.Abs(location.HeightPc) > 3.0 * galaxy.ThinDiskScaleHeightPc)
        {
            return false;
        }

        if (location.SupernovaRateRelativeToSolar > MaximumSafeSupernovaRate) return false;
        return CanHostIron(location.MetallicityFeH) && CanHostOre(location.MetallicityFeH);
    }

    public static bool CanHostIron(double feH) => feH >= IronCoreMinimumFeH;

    public static bool CanHostOre(double feH) => feH >= OreFormingMinimumFeH;

    public static double MeanFeH(GalaxyBlueprint galaxy, double radiusKpc)
        => galaxy.SolarAnalogMetallicityFeH
           + galaxy.MetallicityGradientDexPerKpc * (radiusKpc - galaxy.MetallicityReferenceRadiusKpc);

    public static double StructuralRadiusKpc(GalaxyBlueprint galaxy, GalacticLocation location)
        => StructuralRadiusKpc(galaxy, location.GalactocentricRadiusKpc, location.HeightPc);

    public static double StructuralRadiusKpc(GalaxyBlueprint galaxy, double cylindricalRadiusKpc, double heightPc)
    {
        if (!galaxy.IsElliptical) return cylindricalRadiusKpc;

        double zKpc = heightPc / 1000.0;
        double flattenedZ = zKpc / Math.Max(0.2, galaxy.AxisRatio);
        return DetMath.Sqrt((cylindricalRadiusKpc * cylindricalRadiusKpc) + (flattenedZ * flattenedZ));
    }

    /// <summary>Azimuth of a logarithmic spiral arm at a given radius, for figures and arm tests.</summary>
    public static double SpiralArmAngleRad(GalaxyBlueprint galaxy, int armIndex, double radiusKpc)
    {
        if (galaxy.SpiralArmCount <= 0) return 0.0;

        double pitchRad = galaxy.SpiralPitchDeg * (Math.PI / 180.0);
        double logTerm = DetLog(Math.Max(0.5, radiusKpc) / SolarNeighborhoodRadiusKpc);
        double armPhase = logTerm / DetTan(Math.Max(0.05, pitchRad));
        return (2.0 * Math.PI * armIndex / galaxy.SpiralArmCount) + armPhase;
    }

    public static bool IsInSpiralArm(GalaxyBlueprint galaxy, double radiusKpc, double azimuthRad)
        => galaxy.SpiralArmCount > 0
           && NearestArmOffsetRad(galaxy, radiusKpc, azimuthRad) < SpiralArmHalfWidthRad;

    private static HostGalaxy Place(GalaxyBlueprint galaxy, IRng rng)
    {
        GalacticLocation location = galaxy.IsElliptical
            ? SampleEllipticalLocation(galaxy, rng)
            : SampleDiskLocation(galaxy, rng);
        return new HostGalaxy(galaxy, location);
    }

    private static GalaxyBlueprint GenerateSpiral(IRng rng)
    {
        bool barred = rng.Chance(0.65);
        GalaxyMorphology morphology = barred ? GalaxyMorphology.BarredSpiral : GalaxyMorphology.UnbarredSpiral;
        double stellarMassSolar = LogUniform(rng, 3.0e10, 1.2e11);
        double diskScaleLengthKpc = rng.NextDouble(2.2, 4.2);
        double thinDiskScaleHeightPc = rng.NextDouble(220.0, 380.0);
        double bulgeToDiskMass = barred ? rng.NextDouble(0.25, 0.45) : rng.NextDouble(0.12, 0.30);
        double solarAnalog = rng.NextDouble(-0.08, 0.10);
        double gradient = rng.NextDouble(-0.075, -0.045);
        double scatter = rng.NextDouble(0.08, 0.14);
        int armCount = rng.Chance(0.7) ? 4 : 2;
        double pitchDeg = rng.NextDouble(10.0, 18.0);
        double innerHabitable = barred ? rng.NextDouble(5.5, 7.0) : rng.NextDouble(4.5, 6.5);

        var draft = new GalaxyBlueprint(
            morphology,
            stellarMassSolar,
            diskScaleLengthKpc,
            thinDiskScaleHeightPc,
            bulgeToDiskMass,
            solarAnalog,
            gradient,
            scatter,
            armCount,
            pitchDeg,
            innerHabitable,
            OuterHabitableRadiusKpc: 12.0,
            SersicIndex: 1.0,
            AxisRatio: thinDiskScaleHeightPc / (diskScaleLengthKpc * 1000.0),
            MetallicityReferenceRadiusKpc: SolarNeighborhoodRadiusKpc);

        double outerHabitable = Math.Max(innerHabitable + 1.5, OuterHabitableRadiusKpc(draft));
        return draft with { OuterHabitableRadiusKpc = outerHabitable };
    }

    private static GalaxyBlueprint GenerateElliptical(IRng rng)
    {
        double stellarMassSolar = LogUniform(rng, 8.0e10, 6.0e11);
        double effectiveRadiusKpc = rng.NextDouble(2.8, 7.5);
        double sersicIndex = rng.NextDouble(3.2, 4.8);
        double axisRatio = rng.NextDouble(0.55, 0.92);
        double solarAnalog = rng.NextDouble(0.05, 0.32);
        double gradient = rng.NextDouble(-0.12, -0.05);
        double scatter = rng.NextDouble(0.10, 0.18);
        double innerHabitable = rng.NextDouble(0.45, 0.75) * effectiveRadiusKpc;

        var draft = new GalaxyBlueprint(
            GalaxyMorphology.Elliptical,
            stellarMassSolar,
            DiskScaleLengthKpc: effectiveRadiusKpc,
            ThinDiskScaleHeightPc: axisRatio * effectiveRadiusKpc * 1000.0,
            BulgeToDiskMass: 1.0,
            solarAnalog,
            gradient,
            scatter,
            SpiralArmCount: 0,
            SpiralPitchDeg: 0.0,
            innerHabitable,
            OuterHabitableRadiusKpc: 12.0,
            SersicIndex: sersicIndex,
            AxisRatio: axisRatio,
            MetallicityReferenceRadiusKpc: effectiveRadiusKpc);

        double outerHabitable = Math.Max(innerHabitable + 0.8, OuterHabitableRadiusKpc(draft));
        return draft with { OuterHabitableRadiusKpc = outerHabitable };
    }

    private static double OuterHabitableRadiusKpc(GalaxyBlueprint galaxy)
    {
        double gradient = galaxy.MetallicityGradientDexPerKpc;
        if (gradient >= 0.0) return 15.0;

        double radius = galaxy.MetallicityReferenceRadiusKpc
                        + (OreFormingMinimumFeH + galaxy.MetallicityScatterDex - galaxy.SolarAnalogMetallicityFeH)
                        / gradient;
        double ceiling = galaxy.IsElliptical ? 20.0 : 16.0;
        return DetMath.Clamp(radius, galaxy.InnerHabitableRadiusKpc + 0.5, ceiling);
    }

    private static GalacticLocation SampleDiskLocation(GalaxyBlueprint galaxy, IRng rng)
    {
        for (int attempt = 0; attempt < MaxLocationAttempts; attempt++)
        {
            double radiusKpc = SampleAreaWeightedRadius(
                galaxy.InnerHabitableRadiusKpc,
                galaxy.OuterHabitableRadiusKpc,
                rng);
            double azimuthRad = rng.NextDouble(0.0, 2.0 * Math.PI);
            double heightPc = Gaussian(rng) * (galaxy.ThinDiskScaleHeightPc / DetMath.Sqrt(2.0));
            GalacticLocation location = CreateLocation(galaxy, radiusKpc, azimuthRad, heightPc, rng);
            if (IsHabitable(galaxy, location)) return location;
        }

        return FallbackLocation(galaxy);
    }

    private static GalacticLocation SampleEllipticalLocation(GalaxyBlueprint galaxy, IRng rng)
    {
        for (int attempt = 0; attempt < MaxLocationAttempts; attempt++)
        {
            double structuralRadiusKpc = SampleVolumeWeightedRadius(
                galaxy.InnerHabitableRadiusKpc,
                galaxy.OuterHabitableRadiusKpc,
                rng);
            double azimuthRad = rng.NextDouble(0.0, 2.0 * Math.PI);
            double mu = rng.NextDouble(-1.0, 1.0);
            double cylindricalRadiusKpc = structuralRadiusKpc * DetMath.Sqrt(Math.Max(0.0, 1.0 - (mu * mu)));
            double heightPc = structuralRadiusKpc * mu * galaxy.AxisRatio * 1000.0;
            GalacticLocation location = CreateLocation(galaxy, cylindricalRadiusKpc, azimuthRad, heightPc, rng);
            if (IsHabitable(galaxy, location)) return location;
        }

        return FallbackLocation(galaxy);
    }

    private static GalacticLocation CreateLocation(
        GalaxyBlueprint galaxy,
        double radiusKpc,
        double azimuthRad,
        double heightPc,
        IRng rng)
    {
        double structural = StructuralRadiusKpc(galaxy, radiusKpc, heightPc);
        double feH = MeanFeH(galaxy, structural) + (Gaussian(rng) * galaxy.MetallicityScatterDex);
        double density = StellarDensityRelativeToSolar(galaxy, structural, heightPc);
        double supernovaRate = density * (0.65 + (0.35 * (galaxy.InnerHabitableRadiusKpc / Math.Max(0.4, structural))));
        return new GalacticLocation(
            radiusKpc,
            azimuthRad,
            heightPc,
            feH,
            InSpiralArm: IsInSpiralArm(galaxy, radiusKpc, azimuthRad),
            LocalStellarDensityRelativeToSolar: density,
            SupernovaRateRelativeToSolar: supernovaRate);
    }

    private static GalacticLocation FallbackLocation(GalaxyBlueprint galaxy)
    {
        double radiusKpc = 0.5 * (galaxy.InnerHabitableRadiusKpc + galaxy.OuterHabitableRadiusKpc);
        return new GalacticLocation(
            radiusKpc,
            0.0,
            20.0,
            Math.Max(OreFormingMinimumFeH, galaxy.SolarAnalogMetallicityFeH),
            InSpiralArm: false,
            LocalStellarDensityRelativeToSolar: 1.0,
            SupernovaRateRelativeToSolar: 1.0);
    }

    private static double StellarDensityRelativeToSolar(
        GalaxyBlueprint galaxy,
        double structuralRadiusKpc,
        double heightPc)
    {
        if (galaxy.IsElliptical)
        {
            double re = Math.Max(0.5, galaxy.DiskScaleLengthKpc);
            double n = Math.Max(1.0, galaxy.SersicIndex);
            double b = (1.9992 * n) - 0.3271;
            double ratio = Math.Max(0.05, structuralRadiusKpc / re);
            double intensity = DetExp(-b * (DetPow(ratio, 1.0 / n) - 1.0));
            return DetMath.Clamp(intensity, 1e-4, 40.0);
        }

        double radial = DetExp(-(structuralRadiusKpc - SolarNeighborhoodRadiusKpc) / galaxy.DiskScaleLengthKpc);
        double vertical = DetExp(-Math.Abs(heightPc) / galaxy.ThinDiskScaleHeightPc);
        return radial * vertical;
    }

    private static double NearestArmOffsetRad(GalaxyBlueprint galaxy, double radiusKpc, double azimuthRad)
    {
        double nearest = double.MaxValue;
        for (int arm = 0; arm < galaxy.SpiralArmCount; arm++)
        {
            double delta = AbsolutePrincipalAngle(azimuthRad - SpiralArmAngleRad(galaxy, arm, radiusKpc));
            if (delta < nearest) nearest = delta;
        }

        return nearest;
    }

    /// <summary>Absolute value of an angle wrapped to (−π, π].</summary>
    private static double AbsolutePrincipalAngle(double radians)
    {
        double twoPi = 2.0 * Math.PI;
        double wrapped = radians - (twoPi * Math.Floor((radians + Math.PI) / twoPi));
        return wrapped < 0.0 ? -wrapped : wrapped;
    }

    private static double SampleAreaWeightedRadius(double innerKpc, double outerKpc, IRng rng)
    {
        double innerSq = innerKpc * innerKpc;
        double outerSq = outerKpc * outerKpc;
        return DetMath.Sqrt(innerSq + ((outerSq - innerSq) * rng.NextDouble()));
    }

    private static double SampleVolumeWeightedRadius(double innerKpc, double outerKpc, IRng rng)
    {
        double innerCu = innerKpc * innerKpc * innerKpc;
        double outerCu = outerKpc * outerKpc * outerKpc;
        return CubeRoot(innerCu + ((outerCu - innerCu) * rng.NextDouble()));
    }

    private static double LogUniform(IRng rng, double min, double max)
        => DetExp(DetLog(min) + (rng.NextDouble() * (DetLog(max) - DetLog(min))));

    /// <summary>Irwin–Hall approximation to N(0,1): twelve unit draws minus six. No logs or cosines.</summary>
    private static double Gaussian(IRng rng)
    {
        double sum = 0.0;
        for (int i = 0; i < 12; i++)
        {
            sum += rng.NextDouble();
        }

        return sum - 6.0;
    }

    private static double CubeRoot(double value)
    {
        if (value <= 0.0) return 0.0;
        double guess = value > 1.0 ? value : 1.0;
        for (int i = 0; i < 24; i++)
        {
            guess = ((2.0 * guess) + (value / (guess * guess))) / 3.0;
        }

        return guess;
    }

    private static double DetPow(double value, double exponent)
    {
        if (value <= 0.0) return 0.0;
        return DetExp(exponent * DetLog(value));
    }

    private const double Ln2 = 0.693147180559945309417;

    /// <summary>
    /// Range-reduced Taylor exp. Only <c>+ * /</c>, so the result is bit-identical across runtimes.
    /// </summary>
    private static double DetExp(double x)
    {
        if (x > 700.0) return double.PositiveInfinity;
        if (x < -700.0) return 0.0;

        double nReal = x / Ln2;
        int n = (int)(nReal >= 0.0 ? nReal + 0.5 : nReal - 0.5);
        double r = x - (n * Ln2);
        double term = 1.0;
        double sum = 1.0;
        for (int i = 1; i <= 14; i++)
        {
            term *= r / i;
            sum += term;
        }

        return sum * ScaleByPowerOfTwo(n);
    }

    /// <summary>Natural log via mantissa reduction to [1, 2) and an atanh series.</summary>
    private static double DetLog(double x)
    {
        if (x <= 0.0) return double.NegativeInfinity;

        int exp = 0;
        while (x >= 2.0)
        {
            x *= 0.5;
            exp++;
        }

        while (x < 1.0)
        {
            x *= 2.0;
            exp--;
        }

        double s = (x - 1.0) / (x + 1.0);
        double s2 = s * s;
        double p = s;
        double sum = 0.0;
        for (int i = 0; i < 18; i++)
        {
            sum += p / ((2 * i) + 1);
            p *= s2;
        }

        return (2.0 * sum) + (exp * Ln2);
    }

    /// <summary>Taylor tan, accurate on the 10–18° pitch range used by spiral arms.</summary>
    private static double DetTan(double radians)
    {
        double x2 = radians * radians;
        return radians * (1.0 + (x2 * ((1.0 / 3.0) + (x2 * ((2.0 / 15.0) + (x2 * (17.0 / 315.0)))))));
    }

    private static double ScaleByPowerOfTwo(int n)
    {
        if (n == 0) return 1.0;
        if (n > 0) return DetMath.IntPow(2.0, n);
        return 1.0 / DetMath.IntPow(2.0, -n);
    }
}
