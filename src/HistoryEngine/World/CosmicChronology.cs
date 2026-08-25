using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>The first stellar phase the host reaches after core hydrogen burning ends.</summary>
public enum StellarNextStage
{
    /// <summary>
    /// A fully convective, very-low-mass red dwarf grows hotter and bluer without first
    /// swelling into a red giant.
    /// </summary>
    BlueDwarf = 0,

    /// <summary>A star with a hydrogen-exhausted core expands and brightens before becoming a giant.</summary>
    Subgiant = 1,
}

/// <summary>
/// Deep time before the chronicle and the next physical transition after it.
/// </summary>
/// <remarks>
/// <para>The chronicle's integer years are civic time. They cannot also be dates for events
/// billions of years apart, so these are lookback intervals measured from year one. The ordering
/// is the contract: the galaxy begins assembling, earlier stars enrich its gas, the host star and
/// disk form, and only then does the history world finish accreting.</para>
///
/// <para>The ages are rolled on their own seed stream after the system has been built. Adding or
/// revising this chronology therefore cannot move a planet, change a comet, or consume a draw that
/// any simulated people use.</para>
/// </remarks>
public sealed record CosmicChronology(
    double UniverseAgeGyr,
    double GalaxyFormationLookbackGyr,
    double StarFormationLookbackGyr,
    double WorldFormationLookbackGyr,
    double PriorStellarEnrichmentGyr,
    double WorldFormationDelayMyr,
    double MainSequenceRemainingGyr,
    StellarNextStage NextStage,
    string StellarFuture)
{
    /// <summary>Current best rounded age used as the outer bound of generated deep time.</summary>
    public const double ObservableUniverseAgeGyr = 13.8;

    /// <summary>
    /// The gas that makes this metal-rich system must have spent at least this long being cycled
    /// through earlier stars. It is a conservative floor, not a claim that enrichment stops here.
    /// </summary>
    public const double MinimumPriorEnrichmentGyr = 1.0;

    /// <summary>Very-low-mass stars below this limit avoid a conventional red-giant phase.</summary>
    public const double BlueDwarfMaximumMassSolar = 0.25;

    /// <summary>Young rocky worlds finish their last large accretion over tens of Myr.</summary>
    public const double MinimumWorldFormationDelayMyr = 10.0;
    public const double MaximumWorldFormationDelayMyr = 100.0;

    public string NextStageLabel => NextStage switch
    {
        StellarNextStage.BlueDwarf => "blue dwarf",
        StellarNextStage.Subgiant => "subgiant",
        _ => NextStage.ToString(),
    };

    /// <summary>Builds an ordered chronology from the already-rolled galaxy and host star.</summary>
    public static CosmicChronology From(
        ulong seed,
        HostGalaxy galaxy,
        double starMassSolar,
        double starLifespanGyr)
    {
        IRng rng = new Pcg32(Hash.Combine(seed, Hash.OfString("world.cosmology.chronology")));

        // "Began assembling" rather than "was created": a galaxy grows hierarchically. Massive,
        // old ellipticals usually assemble their stellar populations earlier than disks, while
        // both ranges stay safely younger than the universe.
        double galaxyMin = galaxy.IsElliptical ? 12.2 : 11.5;
        double galaxyMax = galaxy.IsElliptical ? 13.3 : 13.1;
        double galaxyAge = rng.NextDouble(galaxyMin, galaxyMax);

        // A culture-bearing world needs an established main-sequence star, while the more massive
        // F stars in the roll cannot be assigned an age longer than their short fuel budget. The
        // 72% ceiling leaves every host securely before core-hydrogen exhaustion.
        double maximumStarAge = Math.Min(
            8.0,
            Math.Min(
                galaxyAge - MinimumPriorEnrichmentGyr,
                starLifespanGyr * 0.72));
        double minimumStarAge = Math.Min(3.5, Math.Max(2.2, maximumStarAge * 0.70));
        double starAge = maximumStarAge <= minimumStarAge
            ? maximumStarAge
            : rng.NextDouble(minimumStarAge, maximumStarAge);

        double formationDelayMyr = rng.NextDouble(
            MinimumWorldFormationDelayMyr,
            MaximumWorldFormationDelayMyr);
        double worldAge = starAge - (formationDelayMyr / 1000.0);
        double remaining = Math.Max(0.0, starLifespanGyr - starAge);
        double enrichment = galaxyAge - starAge;

        StellarNextStage next = starMassSolar <= BlueDwarfMaximumMassSolar
            ? StellarNextStage.BlueDwarf
            : StellarNextStage.Subgiant;

        string future = next == StellarNextStage.BlueDwarf
            ? "The fully convective star will grow hotter and bluer without a red-giant phase. "
              + "Its habitable zone will move outward and this world will eventually overheat "
              + "before the star ends as a helium white dwarf; it will not explode as a supernova."
            : "Core-hydrogen exhaustion will make the star a subgiant and then a red giant. "
              + "Its habitable zone will sweep outward, ending surface habitability here, before "
              + "the star sheds its outer layers and remains as a white dwarf; it will not explode "
              + "as a supernova.";

        return new CosmicChronology(
            ObservableUniverseAgeGyr,
            galaxyAge,
            starAge,
            worldAge,
            enrichment,
            formationDelayMyr,
            remaining,
            next,
            future);
    }
}
