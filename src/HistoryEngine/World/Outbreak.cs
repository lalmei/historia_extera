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

    public int Dead { get; set; }

    /// <summary>Settlements it has ever reached, including those that have since recovered.</summary>
    public int Reached { get; set; }

    public bool IsBurningOut => Infected.Count == 0;
}

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
