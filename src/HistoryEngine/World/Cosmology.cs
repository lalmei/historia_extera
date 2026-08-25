using System.Globalization;
using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>Spectral type of the host star. All four classes can host liquid-water worlds on the main sequence.</summary>
public enum StarSpectralClass
{
    M = 0,
    K = 1,
    G = 2,
    F = 3,
}

/// <summary>Role of a non-habitable body in the same system as the history world.</summary>
public enum CompanionRole
{
    /// <summary>Rocky world inward of the habitable zone (Mercury/Venus analog).</summary>
    InnerRocky = 0,

    /// <summary>
    /// Gas giant beyond the snow line. Scatters leftover planetesimals so the habitable
    /// world is not late-bombarded for gigayears.
    /// </summary>
    ShepherdGiant = 1,

    /// <summary>Ice giant outward of the shepherd (Uranus/Neptune analog).</summary>
    OuterIceGiant = 2,
}

/// <summary>A planet that is not the history world, placed for dynamical consistency.</summary>
public sealed record CompanionPlanet(
    CompanionRole Role,
    double SemiMajorAxisAu,
    double MassEarth,
    double RadiusEarth,
    double OrbitalPeriodDays);

/// <summary>One satellite of the parent giant, including the habitable world when history is set on a moon.</summary>
public sealed record SystemMoon(
    int Index,
    double OrbitalDistanceEarthRadii,
    double MassEarth,
    double RadiusEarth,
    double DayLengthDays,
    bool Habitable);

/// <summary>A small icy body on an eccentric orbit, rolled for flavour after the planets are placed.</summary>
public sealed record SystemComet(
    int Index,
    double PerihelionAu,
    double AphelionAu,
    double Eccentricity,
    double InclinationDeg,
    double ArgumentOfPeriapsisRad,
    double OrbitalPeriodDays,
    double NucleusRadiusKm,
    double MassEarth)
{
    public double SemiMajorAxisAu => 0.5 * (PerihelionAu + AphelionAu);
}

/// <summary>Outcome of one consistency check in the habitable-world pipeline.</summary>
public sealed record CosmologyCheck(string Label, bool Passed, string Detail);

/// <summary>
/// Physically derived star-system parameters for a habitable planet or exomoon.
/// </summary>
/// <remarks>
/// <para>Built once from the seed before any civilization is founded. The same seed always
/// produces the same cosmology regardless of simulation length or civ count. The host galaxy
/// is rolled on a separate stream, so adding it cannot reshuffle the star or the habitable
/// body.</para>
///
/// <para>The five-step pipeline follows mass–luminosity, habitable-zone placement, body
/// mass/radius, optional tidal dynamics for moons, and an albedo/greenhouse energy balance.
/// Parameters are adjusted until liquid water is viable: star lifespan above 2 Gyr, orbit
/// inside the HZ, escape velocity above 7 km/s, and surface temperature between 273 K and
/// 343 K. Moons additionally require a day length under seven Earth days and an orbit
/// outside the Roche limit. The host galaxy is the same idea one scale up: a habitable
/// annulus with enough iron for a terrestrial crust.</para>
/// </remarks>
public sealed record WorldCosmology(
    StarSpectralClass StarClass,
    WorldKind Kind,
    double StarMassSolar,
    double StarRadiusSolar,
    double LuminositySolar,
    double StarLifespanGyr,
    double HabitableZoneInnerAu,
    double HabitableZoneOuterAu,
    double OrbitalDistanceAu,
    double OrbitalPeriodDays,
    double WorldMassEarth,
    double WorldRadiusEarth,
    double SurfaceGravityG,
    double EscapeVelocityKmS,
    double BondAlbedo,
    double GreenhouseDeltaC,
    double EquilibriumTempK,
    double SurfaceTempK,
    double? ParentGiantMassEarth,
    double? MoonOrbitalDistanceEarthRadii,
    double? MoonDayLengthDays,
    double? RocheLimitEarthRadii,
    double SnowLineAu,
    IReadOnlyList<CompanionPlanet> Companions,
    IReadOnlyList<SystemMoon> Moons,
    int? HabitableMoonIndex,
    HostGalaxy Galaxy,
    IReadOnlyList<SystemComet> Comets,
    CosmicChronology Chronology)
{
    /// <summary>Aligned with <see cref="WorldFlavour"/> — same fork decides moon vs planet.</summary>
    internal const double MoonChance = 0.4;

    /// <summary>Minimum main-sequence lifetime required for complex surface life.</summary>
    public const double MinStarLifespanGyr = 2.0;

    /// <summary>Minimum escape velocity to retain a thick N₂/O₂ atmosphere (km/s).</summary>
    public const double MinEscapeVelocityKmS = 7.0;

    /// <summary>Surface temperatures that permit liquid water (Kelvin).</summary>
    public const double MinSurfaceTempK = 273.0;
    public const double MaxSurfaceTempK = 343.0;

    /// <summary>Maximum tidal day length before the nightside freezes an atmosphere (Earth days).</summary>
    public const double MaxMoonDayDays = 7.0;

    /// <summary>
    /// Mutual Hill radii required between neighbouring planets. Packed systems can sit near 5;
    /// 8 leaves room for a Jupiter-mass body without ejecting the habitable world.
    /// </summary>
    public const double MinHillSeparation = 8.0;

    /// <summary>Earth masses in one solar mass — converts planet masses into the Hill formula.</summary>
    internal const double EarthMassesPerSolar = 332_946.0;

    /// <summary>Earth radii in one solar radius, for the size strip.</summary>
    internal const double EarthRadiiPerSolar = 109.2;

    /// <summary>Earth radii in one AU — converts a giant's Hill sphere into moon-orbit units.</summary>
    internal const double EarthRadiiPerAu = 23_455.0;

    public bool IsHabitable => EvaluateChecks().All(check => check.Passed);

    public IReadOnlyList<CosmologyCheck> Checks => EvaluateChecks();

    /// <summary>English label for the spectral class, e.g. "G-type".</summary>
    public string StarClassLabel => StarClass switch
    {
        StarSpectralClass.M => "M-type",
        StarSpectralClass.K => "K-type",
        StarSpectralClass.G => "G-type",
        StarSpectralClass.F => "F-type",
        _ => StarClass.ToString(),
    };

    /// <summary>
    /// Procedurally builds a consistent habitable system from the seed alone.
    /// </summary>
    public static WorldCosmology From(ulong seed)
    {
        HostGalaxy galaxy = HostGalaxy.From(seed);
        IRng rng = new Pcg32(Hash.Combine(seed, Hash.OfString("world.cosmology")));

        StarSpectralClass starClass = rng.Pick(StarClasses);
        (double minMass, double maxMass) = MassRange(starClass);
        double starMass = rng.NextDouble(minMass, maxMass);

        double luminosity = MassLuminosity(starMass);
        double starRadius = ComputeStarRadiusSolar(starMass);
        double lifespan = StarLifespan(starMass);
        (double innerHz, double outerHz) = HabitableZone(luminosity);

        bool asMoon = rng.Chance(MoonChance);
        WorldKind kind = asMoon ? WorldKind.Moon : WorldKind.Planet;

        double orbitalAu = PickOrbitalDistance(rng, innerHz, outerHz);
        double yearDays = ComputeOrbitalPeriodDays(orbitalAu, starMass);

        double worldMass;
        double worldRadius;
        double? giantMass = null;
        IReadOnlyList<SystemMoon> moons = Array.Empty<SystemMoon>();
        int? habitableMoonIndex = null;
        double? moonOrbitEarthRadii = null;
        double? moonDay = null;
        double? rocheEarthRadii = null;

        if (kind == WorldKind.Planet)
        {
            worldMass = EnsureAtmosphereRetention(rng.NextDouble(0.5, 2.0), WorldKind.Planet);
            worldRadius = WorldRadiusFromMass(worldMass);
        }
        else
        {
            giantMass = rng.NextDouble(100.0, 300.0);
            worldMass = 1.0;
            worldRadius = 1.0;
        }

        double albedo = rng.NextDouble(0.25, 0.35);
        double greenhouse = rng.NextDouble(28.0, 38.0);

        (double eqTemp, double surfTemp, double finalAu, double finalGreenhouse) = BalanceClimate(
            luminosity,
            orbitalAu,
            albedo,
            greenhouse,
            innerHz,
            outerHz);

        orbitalAu = finalAu;
        yearDays = ComputeOrbitalPeriodDays(orbitalAu, starMass);
        eqTemp = ComputeEquilibriumTempK(luminosity, orbitalAu, albedo);
        surfTemp = eqTemp + finalGreenhouse;
        greenhouse = finalGreenhouse;

        if (kind == WorldKind.Moon && giantMass.HasValue)
        {
            moons = PlaceMoonFamily(rng, starMass, orbitalAu, giantMass.Value);
            SystemMoon home = moons.First(moon => moon.Habitable);
            habitableMoonIndex = home.Index;
            worldMass = home.MassEarth;
            worldRadius = home.RadiusEarth;
            moonOrbitEarthRadii = home.OrbitalDistanceEarthRadii;
            moonDay = home.DayLengthDays;
            rocheEarthRadii = ComputeRocheLimitEarthRadii(
                GiantRadiusEarthRadii(giantMass.Value),
                worldRadius);
        }

        double surfaceG = SurfaceGravity(worldMass, worldRadius);
        double escapeKmS = EscapeVelocity(worldMass, worldRadius);

        double habitableMass = kind == WorldKind.Moon ? giantMass ?? worldMass : worldMass;
        double snowLine = SnowLine(luminosity);
        IReadOnlyList<CompanionPlanet> companions = PlaceCompanions(
            rng,
            starMass,
            snowLine,
            innerHz,
            outerHz,
            orbitalAu,
            habitableMass);
        IReadOnlyList<SystemComet> comets = PlaceComets(seed, starMass, companions);
        CosmicChronology chronology = CosmicChronology.From(seed, galaxy, starMass, lifespan);

        return new WorldCosmology(
            starClass,
            kind,
            starMass,
            starRadius,
            luminosity,
            lifespan,
            innerHz,
            outerHz,
            orbitalAu,
            yearDays,
            worldMass,
            worldRadius,
            surfaceG,
            escapeKmS,
            albedo,
            greenhouse,
            eqTemp,
            surfTemp,
            giantMass,
            moonOrbitEarthRadii,
            moonDay,
            rocheEarthRadii,
            snowLine,
            companions,
            moons,
            habitableMoonIndex,
            galaxy,
            comets,
            chronology);
    }

    private IReadOnlyList<CosmologyCheck> EvaluateChecks()
    {
        List<CosmologyCheck> checks = BuildChecks(
            StarLifespanGyr,
            OrbitalDistanceAu,
            HabitableZoneInnerAu,
            HabitableZoneOuterAu,
            EscapeVelocityKmS,
            SurfaceTempK,
            Kind,
            MoonDayLengthDays,
            MoonOrbitalDistanceEarthRadii,
            RocheLimitEarthRadii,
            StarMassSolar,
            Kind == WorldKind.Moon ? ParentGiantMassEarth ?? WorldMassEarth : WorldMassEarth,
            SnowLineAu,
            Companions);

        checks.Add(new CosmologyCheck(
            "Galactic habitable zone",
            HostGalaxy.IsHabitable(Galaxy.Blueprint, Galaxy.Location),
            Invariant(
                $"R {Galaxy.Location.GalactocentricRadiusKpc:F1} kpc, [Fe/H] {Galaxy.Location.MetallicityFeH:+0.00;-0.00}")));
        checks.Add(new CosmologyCheck(
            "Metals for a crust",
            Galaxy.CanHostIronCore && Galaxy.CanHostOres,
            Invariant(
                $"iron {(Galaxy.CanHostIronCore ? "yes" : "no")}, ores {(Galaxy.CanHostOres ? "yes" : "no")}")));
        return checks;
    }

    private static double EnsureAtmosphereRetention(double massEarth, WorldKind kind)
    {
        double max = kind == WorldKind.Planet ? 2.0 : 1.0;
        double mass = massEarth;

        for (int i = 0; i < 16 && EscapeVelocity(mass, WorldRadiusFromMass(mass)) < MinEscapeVelocityKmS; i++)
        {
            mass = DetMath.Clamp(mass * 1.08, massEarth, max);
        }

        return mass;
    }

    private static readonly StarSpectralClass[] StarClasses =
    {
        StarSpectralClass.M,
        StarSpectralClass.K,
        StarSpectralClass.G,
        StarSpectralClass.F,
    };

    /// <summary>Main-sequence mass range (solar masses) for each spectral class.</summary>
    internal static (double Min, double Max) MassRange(StarSpectralClass starClass) => starClass switch
    {
        StarSpectralClass.M => (0.08, 0.45),
        StarSpectralClass.K => (0.45, 0.80),
        StarSpectralClass.G => (0.80, 1.04),
        StarSpectralClass.F => (1.04, 1.40),
        _ => (0.80, 1.04),
    };

    /// <summary>L_* ≈ M_*^3.5 in solar units.</summary>
    internal static double MassLuminosity(double massSolar) =>
        DetMath.IntPow(massSolar, 3) * DetMath.Sqrt(massSolar);

    /// <summary>Main-sequence radius R_* ≈ M_*^0.8, in solar radii.</summary>
    internal static double ComputeStarRadiusSolar(double massSolar) => RationalPow(massSolar, 4, 5);

    /// <summary>T ≈ 10 × (1/M_*)^2.5 billion years.</summary>
    internal static double StarLifespan(double massSolar)
    {
        if (massSolar <= 0.0) return 0.0;
        return 10.0 * DetMath.IntPow(1.0 / massSolar, 2) * DetMath.Sqrt(1.0 / massSolar);
    }

    /// <summary>Inner and outer HZ edges (AU) from stellar luminosity.</summary>
    internal static (double Inner, double Outer) HabitableZone(double luminositySolar)
    {
        double inner = DetMath.Sqrt(luminositySolar / 1.1);
        double outer = DetMath.Sqrt(luminositySolar / 0.53);
        return (inner, outer);
    }

    /// <summary>
    /// Water-ice condensation line, ~2.7 AU around the Sun, scaled with sqrt(L_*).
    /// A gas giant that accretes beyond this line can grow large enough to scatter leftover rock.
    /// </summary>
    internal static double SnowLine(double luminositySolar) => 2.7 * DetMath.Sqrt(luminositySolar);

    /// <summary>
    /// Mutual Hill radius of two planets. Long-term stability wants several of these between orbits.
    /// </summary>
    internal static double MutualHillAu(
        double a1,
        double mass1Earth,
        double a2,
        double mass2Earth,
        double starMassSolar)
    {
        double meanA = (a1 + a2) * 0.5;
        double massSolar = (mass1Earth + mass2Earth) / EarthMassesPerSolar;
        return meanA * NthRoot(massSolar / (3.0 * starMassSolar), 3);
    }

    internal static bool HillSeparated(
        double a1,
        double mass1Earth,
        double a2,
        double mass2Earth,
        double starMassSolar) =>
        Math.Abs(a2 - a1) >= MinHillSeparation * MutualHillAu(a1, mass1Earth, a2, mass2Earth, starMassSolar);

    internal static double ComputeOrbitalPeriodDays(double semiMajorAxisAu, double starMassSolar) =>
        365.25 * DetMath.Sqrt(DetMath.IntPow(semiMajorAxisAu, 3) / starMassSolar);

    internal static double WorldRadiusFromMass(double massEarth) =>
        RationalPow(massEarth, 27, 100);

    internal static double SurfaceGravity(double massEarth, double radiusEarth) =>
        massEarth / DetMath.IntPow(radiusEarth, 2);

    internal static double EscapeVelocity(double massEarth, double radiusEarth) =>
        11.2 * DetMath.Sqrt(massEarth / radiusEarth);

    internal static double GiantRadiusEarthRadii(double massEarth) =>
        2.0 * DetMath.Sqrt(DetMath.Sqrt(massEarth));

    internal static double ComputeRocheLimitEarthRadii(double giantRadiusEarth, double moonRadiusEarth)
    {
        const double giantDensity = 700.0;
        const double moonDensity = 5514.0;
        double ratio = NthRoot(giantDensity / moonDensity, 3);
        return 2.44 * giantRadiusEarth * ratio;
    }

    internal static double MoonOrbitalPeriodDays(double giantMassEarth, double moonOrbitEarthRadii)
    {
        double giantMassSolar = giantMassEarth / EarthMassesPerSolar;
        double moonOrbitAu = moonOrbitEarthRadii / EarthRadiiPerAu;
        return ComputeOrbitalPeriodDays(moonOrbitAu, giantMassSolar);
    }

    /// <summary>Hill sphere of the parent giant, in Earth radii — moons must sit well inside it.</summary>
    internal static double GiantHillSphereEarthRadii(
        double giantAu,
        double giantMassEarth,
        double starMassSolar)
    {
        double massRatio = (giantMassEarth / EarthMassesPerSolar) / (3.0 * starMassSolar);
        return giantAu * NthRoot(massRatio, 3) * EarthRadiiPerAu;
    }

    internal static double ComputeEquilibriumTempK(double luminositySolar, double orbitalAu, double albedo)
    {
        double starTerm = DetMath.Sqrt(DetMath.Sqrt(luminositySolar / DetMath.IntPow(orbitalAu, 2)));
        double albedoTerm = DetMath.Sqrt(DetMath.Sqrt(1.0 - albedo));
        return 278.0 * starTerm * albedoTerm;
    }

    private static double PickOrbitalDistance(IRng rng, double innerHz, double outerHz)
    {
        double span = outerHz - innerHz;
        double center = innerHz + (0.35 * span) + (rng.NextDouble() * 0.30 * span);
        return DetMath.Clamp(center, innerHz * 1.02, outerHz * 0.98);
    }

    private static IReadOnlyList<SystemMoon> PlaceMoonFamily(
        IRng rng,
        double starMassSolar,
        double giantAu,
        double giantMassEarth)
    {
        double giantRadius = GiantRadiusEarthRadii(giantMassEarth);
        double roche = ComputeRocheLimitEarthRadii(giantRadius, WorldRadiusFromMass(0.4));
        double hill = GiantHillSphereEarthRadii(giantAu, giantMassEarth, starMassSolar);
        double inner = roche * 1.12;
        double dayLimit = MaxHabitableMoonOrbitEarthRadii(giantMassEarth);
        double outer = hill * 0.36;
        if (dayLimit < outer) outer = dayLimit;
        if (outer < inner) outer = inner;

        int count = 1;
        if (outer > inner * 1.18)
        {
            double span = outer / inner;
            for (int n = 2; n <= 8; n++)
            {
                if (NthRoot(span, n - 1) < 1.18) break;
                count = n;
            }

            count = rng.NextInt(1, count + 1);
        }

        double factor = count == 1 ? 1.0 : NthRoot(outer / inner, count - 1);
        var orbits = new double[count];
        for (int i = 0; i < count; i++)
        {
            orbits[i] = inner * DetMath.IntPow(factor, i);
        }

        int home = rng.NextInt(count);
        var moons = new List<SystemMoon>(count);

        for (int i = 0; i < count; i++)
        {
            bool habitable = i == home;
            double mass = habitable
                ? EnsureAtmosphereRetention(rng.NextDouble(0.12, 1.0), WorldKind.Moon)
                : rng.NextDouble(0.008, 0.06);
            double radius = WorldRadiusFromMass(mass);
            moons.Add(new SystemMoon(
                Index: i + 1,
                OrbitalDistanceEarthRadii: orbits[i],
                MassEarth: mass,
                RadiusEarth: radius,
                DayLengthDays: MoonOrbitalPeriodDays(giantMassEarth, orbits[i]),
                Habitable: habitable));
        }

        return moons;
    }

    /// <summary>
    /// Farthest moon orbit whose tidally locked day is still under
    /// <see cref="MaxMoonDayDays"/>. Every satellite of the parent must sit inside this,
    /// not only the habitable one — otherwise "the 8th moon" is a frozen nightside.
    /// </summary>
    internal static double MaxHabitableMoonOrbitEarthRadii(double giantMassEarth)
    {
        double giantMassSolar = giantMassEarth / EarthMassesPerSolar;
        double periodRatio = MaxMoonDayDays / 365.25;
        double au = NthRoot(giantMassSolar * periodRatio * periodRatio, 3);
        return au * EarthRadiiPerAu * 0.995;
    }

    private static (double EqTemp, double SurfTemp, double OrbitalAu, double Greenhouse) BalanceClimate(
        double luminosity,
        double orbitalAu,
        double albedo,
        double greenhouse,
        double innerHz,
        double outerHz)
    {
        double au = orbitalAu;
        double gh = greenhouse;

        for (int pass = 0; pass < 16; pass++)
        {
            double eq = ComputeEquilibriumTempK(luminosity, au, albedo);
            double surf = eq + gh;

            if (surf >= MinSurfaceTempK && surf <= MaxSurfaceTempK)
            {
                return (eq, surf, au, gh);
            }

            if (surf < MinSurfaceTempK)
            {
                gh += 4.0;
                if (gh > 55.0 && au > innerHz * 1.01)
                {
                    au *= 0.96;
                    gh = greenhouse;
                }
            }
            else
            {
                gh -= 4.0;
                if (gh < 10.0 && au < outerHz * 0.99)
                {
                    au *= 1.04;
                    gh = greenhouse;
                }
            }

            au = DetMath.Clamp(au, innerHz * 1.01, outerHz * 0.99);
            gh = DetMath.Clamp(gh, 8.0, 60.0);
        }

        double finalEq = ComputeEquilibriumTempK(luminosity, au, albedo);
        return (finalEq, finalEq + gh, au, gh);
    }

    private static IReadOnlyList<CompanionPlanet> PlaceCompanions(
        IRng rng,
        double starMassSolar,
        double snowLineAu,
        double innerHz,
        double outerHz,
        double habitableAu,
        double habitableMassEarth)
    {
        var placed = new List<CompanionPlanet>(3);

        CompanionPlanet shepherd = PlaceShepherd(
            rng, starMassSolar, snowLineAu, outerHz, habitableAu, habitableMassEarth);
        placed.Add(shepherd);

        if (rng.Chance(0.55))
        {
            double innerAu = rng.NextDouble(innerHz * 0.32, innerHz * 0.82);
            double innerMass = rng.NextDouble(0.06, 0.70);
            if (innerAu > 0.03
                && HillSeparated(innerAu, innerMass, habitableAu, habitableMassEarth, starMassSolar))
            {
                placed.Add(new CompanionPlanet(
                    CompanionRole.InnerRocky,
                    innerAu,
                    innerMass,
                    WorldRadiusFromMass(innerMass),
                    ComputeOrbitalPeriodDays(innerAu, starMassSolar)));
            }
        }

        if (rng.Chance(0.40))
        {
            double iceAu = shepherd.SemiMajorAxisAu * rng.NextDouble(1.65, 2.55);
            double iceMass = rng.NextDouble(14.0, 22.0);
            if (HillSeparated(
                    shepherd.SemiMajorAxisAu,
                    shepherd.MassEarth,
                    iceAu,
                    iceMass,
                    starMassSolar))
            {
                placed.Add(new CompanionPlanet(
                    CompanionRole.OuterIceGiant,
                    iceAu,
                    iceMass,
                    IceGiantRadius(iceMass),
                    ComputeOrbitalPeriodDays(iceAu, starMassSolar)));
            }
        }

        placed.Sort((a, b) => a.SemiMajorAxisAu.CompareTo(b.SemiMajorAxisAu));
        return placed;
    }

    private static CompanionPlanet PlaceShepherd(
        IRng rng,
        double starMassSolar,
        double snowLineAu,
        double outerHz,
        double habitableAu,
        double habitableMassEarth)
    {
        // Beyond the snow line so ice can feed runaway accretion; outside the HZ so it is not
        // a second habitable-zone occupant; and at least ~1.8× the habitable orbit so a
        // Jupiter-mass Hill sphere cannot chew the world's path.
        double minAu = snowLineAu * 1.15;
        if (outerHz * 1.25 > minAu) minAu = outerHz * 1.25;
        if (habitableAu * 1.8 > minAu) minAu = habitableAu * 1.8;

        double maxAu = snowLineAu * 2.4;
        if (maxAu < minAu * 1.15) maxAu = minAu * 1.35;

        double au = rng.NextDouble(minAu, maxAu);
        double mass = rng.NextDouble(120.0, 320.0);

        for (int i = 0; i < 20
            && !HillSeparated(habitableAu, habitableMassEarth, au, mass, starMassSolar);
            i++)
        {
            au *= 1.08;
        }

        return new CompanionPlanet(
            CompanionRole.ShepherdGiant,
            au,
            mass,
            GiantRadiusEarthRadii(mass),
            ComputeOrbitalPeriodDays(au, starMassSolar));
    }

    /// <summary>
    /// Notable comets on their own stream, so adding a tail cannot reshuffle the planets.
    /// A few Jupiter-family paths hug the shepherd; the rest are Halley-type or long-period.
    /// </summary>
    private static IReadOnlyList<SystemComet> PlaceComets(
        ulong seed,
        double starMassSolar,
        IReadOnlyList<CompanionPlanet> companions)
    {
        IRng rng = new Pcg32(Hash.Combine(seed, Hash.OfString("world.cosmology.comets")));
        double shepherdAu = 5.2;
        foreach (CompanionPlanet body in companions)
        {
            if (body.Role == CompanionRole.ShepherdGiant)
            {
                shepherdAu = body.SemiMajorAxisAu;
                break;
            }
        }

        int count = rng.NextInt(2, 6);
        var comets = new List<SystemComet>(count);
        for (int i = 0; i < count; i++)
        {
            double perihelionAu;
            double aphelionAu;
            double roll = rng.NextDouble();
            if (roll < 0.50)
            {
                perihelionAu = rng.NextDouble(0.40, 1.80);
                aphelionAu = shepherdAu * rng.NextDouble(0.85, 1.45);
            }
            else if (roll < 0.80)
            {
                perihelionAu = rng.NextDouble(0.30, 1.20);
                aphelionAu = rng.NextDouble(12.0, 38.0);
            }
            else
            {
                perihelionAu = rng.NextDouble(0.25, 2.40);
                aphelionAu = rng.NextDouble(45.0, 180.0);
            }

            if (aphelionAu < perihelionAu + 0.4)
            {
                aphelionAu = perihelionAu + 0.4;
            }

            double semiMajor = 0.5 * (perihelionAu + aphelionAu);
            double eccentricity = (aphelionAu - perihelionAu) / (aphelionAu + perihelionAu);
            double nucleusKm = rng.NextDouble(1.2, 14.0);
            comets.Add(new SystemComet(
                Index: i + 1,
                perihelionAu,
                aphelionAu,
                eccentricity,
                InclinationDeg: rng.NextDouble(4.0, 162.0),
                ArgumentOfPeriapsisRad: rng.NextDouble(0.0, 2.0 * Math.PI),
                OrbitalPeriodDays: ComputeOrbitalPeriodDays(semiMajor, starMassSolar),
                NucleusRadiusKm: nucleusKm,
                MassEarth: CometMassEarth(nucleusKm)));
        }

        return comets;
    }

    /// <summary>Ice-rich nucleus at 600 kg/m³, in Earth masses.</summary>
    internal static double CometMassEarth(double nucleusRadiusKm)
    {
        const double densityKgM3 = 600.0;
        const double earthKg = 5.972e24;
        double radiusM = nucleusRadiusKm * 1000.0;
        double volume = (4.0 / 3.0) * Math.PI * radiusM * radiusM * radiusM;
        return volume * densityKgM3 / earthKg;
    }

    internal static double IceGiantRadius(double massEarth) =>
        3.2 * DetMath.Sqrt(DetMath.Sqrt(massEarth / 17.0));

    private static List<CosmologyCheck> BuildChecks(
        double lifespan,
        double orbitalAu,
        double innerHz,
        double outerHz,
        double escapeKmS,
        double surfTemp,
        WorldKind kind,
        double? moonDay,
        double? moonOrbit,
        double? roche,
        double starMassSolar,
        double habitableMassEarth,
        double snowLineAu,
        IReadOnlyList<CompanionPlanet> companions)
    {
        var checks = new List<CosmologyCheck>(8);

        checks.Add(new CosmologyCheck(
            "Star lifespan",
            lifespan >= MinStarLifespanGyr,
            Invariant($"{lifespan:F1} Gyr (need ≥ {MinStarLifespanGyr:F0} Gyr)")));

        bool inHz = orbitalAu >= innerHz && orbitalAu <= outerHz;
        checks.Add(new CosmologyCheck(
            "Habitable zone",
            inHz,
            Invariant($"{orbitalAu:F2} AU (HZ {innerHz:F2}–{outerHz:F2} AU)")));

        checks.Add(new CosmologyCheck(
            "Atmosphere retention",
            escapeKmS >= MinEscapeVelocityKmS,
            Invariant($"{escapeKmS:F1} km/s escape (need ≥ {MinEscapeVelocityKmS:F1} km/s)")));

        bool tempOk = surfTemp >= MinSurfaceTempK && surfTemp <= MaxSurfaceTempK;
        checks.Add(new CosmologyCheck(
            "Surface temperature",
            tempOk,
            Invariant($"{surfTemp - 273.15:F0} °C ({surfTemp:F0} K)")));

        if (kind == WorldKind.Moon)
        {
            bool rocheOk = moonOrbit.HasValue && roche.HasValue && moonOrbit.Value > roche.Value;
            checks.Add(new CosmologyCheck(
                "Roche limit",
                rocheOk,
                rocheOk
                    ? Invariant($"Moon at {moonOrbit!.Value:F0} R⊕, limit {roche!.Value:F0} R⊕")
                    : "Moon orbit inside Roche limit"));

            bool dayOk = moonDay.HasValue && moonDay.Value <= MaxMoonDayDays;
            checks.Add(new CosmologyCheck(
                "Tidal day length",
                dayOk,
                moonDay.HasValue
                    ? Invariant($"{moonDay.Value:F1} Earth days (max {MaxMoonDayDays:F0})")
                    : "Unknown"));
        }

        CompanionPlanet? shepherd = null;
        foreach (CompanionPlanet body in companions)
        {
            if (body.Role == CompanionRole.ShepherdGiant)
            {
                shepherd = body;
                break;
            }
        }

        bool hasShepherd = shepherd is not null;
        bool beyondSnow = hasShepherd && shepherd!.SemiMajorAxisAu > snowLineAu;
        bool hillOk = hasShepherd
            && HillSeparated(
                orbitalAu,
                habitableMassEarth,
                shepherd!.SemiMajorAxisAu,
                shepherd.MassEarth,
                starMassSolar);

        checks.Add(new CosmologyCheck(
            "Shepherd giant",
            hasShepherd && beyondSnow && hillOk,
            hasShepherd
                ? Invariant(
                    $"{shepherd!.MassEarth:F0} M⊕ at {shepherd.SemiMajorAxisAu:F2} AU (snow line {snowLineAu:F2} AU)")
                : "No outer giant to scatter leftover planetesimals"));

        return checks;
    }

    private static string Invariant(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>value^(num/den) using only sqrt and integer powers — safe on decision paths.</summary>
    private static double RationalPow(double value, int num, int den)
    {
        if (value <= 0.0) return 0.0;
        if (num == 0) return 1.0;

        double magnitude = num > 0
            ? DetMath.IntPow(value, num)
            : 1.0 / DetMath.IntPow(value, -num);

        return NthRoot(magnitude, den);
    }

    private static double NthRoot(double value, int n)
    {
        if (n <= 1) return value;
        if (value <= 0.0) return 0.0;
        if (n == 2) return DetMath.Sqrt(value);
        if (n == 4) return DetMath.Sqrt(DetMath.Sqrt(value));

        double low = 0.0;
        double high = value < 1.0 ? 1.0 : value;

        for (int i = 0; i < 64; i++)
        {
            double mid = (low + high) * 0.5;
            if (DetMath.IntPow(mid, n) < value) low = mid;
            else high = mid;
        }

        return (low + high) * 0.5;
    }
}
