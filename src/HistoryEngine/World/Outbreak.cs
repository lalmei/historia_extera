using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>
/// One epidemic, while it is still running.
/// </summary>
/// <remarks>
/// <para>Carries the state a plague needs between years: where it currently is, how long each
/// place has had it, and what it has cost so far. Once it burns out the object is dropped — the
/// events it wrote are what remains, which is why it is not an entity with an id.</para>
///
/// <para>The name is composed at ignition and repeated in every event the outbreak writes, so a
/// reader follows one plague across a decade and a dozen towns without the engine needing an
/// entity for it to point at.</para>
/// </remarks>
public sealed class Outbreak
{
    public Outbreak(string name, EntityId originId, int startYear, double lethality, double virulence)
    {
        Name = name;
        OriginId = originId;
        StartYear = startYear;
        Lethality = lethality;
        Virulence = virulence;
        Infected = new List<Infection>();
        InTransit = new List<Passage>();
    }

    /// <summary>What it was called: "the Speckled Death".</summary>
    public string Name { get; }

    public EntityId OriginId { get; }

    public int StartYear { get; }

    /// <summary>Fraction of a settlement's people it kills each year it is present.</summary>
    public double Lethality { get; }

    /// <summary>How readily it reaches the next town.</summary>
    public double Virulence { get; }

    public List<Infection> Infected { get; }

    /// <summary>
    /// Where it is currently travelling to, and when it gets there.
    /// </summary>
    /// <remarks>
    /// <para>A plague does not appear in the next town the instant it leaves this one. It goes at
    /// the speed of the people carrying it, which is days along a road and weeks along a trade
    /// route — and a year was far too coarse to say so, since within one tick every jump was
    /// simultaneous however far it went.</para>
    ///
    /// <para>The docket holds the timing and this holds the payload. An entry can name one subject,
    /// which is the town being travelled to; which plague is on the road to it, and from where,
    /// belongs with the plague.</para>
    /// </remarks>
    public List<Passage> InTransit { get; }

    public int Dead { get; set; }

    /// <summary>Settlements it has ever reached, including those that have since recovered.</summary>
    public int Reached { get; set; }

    /// <summary>
    /// Nowhere left that has it, and nobody still carrying it anywhere.
    /// </summary>
    /// <remarks>
    /// The second half is what lets a plague go quiet and come back: every town it had may have
    /// recovered while a carrier is still weeks from the next one, and declaring it over at that
    /// moment would drop the outbreak and lose the arrival it had already earned.
    /// </remarks>
    public bool IsBurningOut => Infected.Count == 0 && InTransit.Count == 0;
}

/// <summary>One plague's journey from a town that has it to a town that does not yet.</summary>
/// <param name="Due">When the carriers arrive. Also the key it is found by when they do.</param>
public sealed record Passage(EntityId SettlementId, EntityId FromId, Stamp Due);

/// <summary>One settlement's bout of one plague.</summary>
public sealed class Infection
{
    public Infection(EntityId settlementId, int since)
    {
        SettlementId = settlementId;
        Since = since;
    }

    public EntityId SettlementId { get; }

    public int Since { get; }
}
