using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>What kind of thing an artifact is. Explicit values — part of the export format.</summary>
public enum ArtifactKind
{
    Regalia = 0,
    Weapon = 1,
    Relic = 2,
    Tome = 3,
    Idol = 4,
    Jewel = 5,
    Clothing = 6,
    Armor = 7,
}

/// <summary>The kind of account preserved inside a <see cref="ArtifactKind.Tome"/>.</summary>
/// <remarks>
/// Explicit values because the kind crosses the export boundary. A tome's kind says what
/// questions its sections answer; the subject id says who or what those answers are about.
/// </remarks>
public enum TomeContentKind
{
    Biography = 0,
    Campaign = 1,
    ReligiousRite = 2,
    ReligiousTeaching = 3,
    Annals = 4,
    ArtifactHistory = 5,
}

/// <summary>One passage in a tome, with the entities a reader can follow from it.</summary>
public sealed record TomeSection(
    string Heading,
    string Text,
    IReadOnlyList<EntityId> References);

/// <summary>A copy made in another settlement from an exemplar already available there.</summary>
/// <remarks>
/// This is a distribution record, not another famous artifact. It says that a copy was made;
/// later abandonment may mean that copy no longer survives.
/// </remarks>
public sealed record TomeCopy(
    int Year,
    EntityId SettlementId,
    EntityId SourceSettlementId);

/// <summary>
/// The contents fixed inside a tome when it was made.
/// </summary>
/// <remarks>
/// Stored rather than rendered from final world state: a campaign still under way when a codex
/// was written must not acquire the peace settlement thirty years later. <see cref="ContextId"/>
/// carries the war for a campaign account and is otherwise empty; section references carry the
/// rest of the people, places and events named by the text.
/// </remarks>
public sealed class TomeContents
{
    public TomeContents(
        TomeContentKind kind,
        EntityId subjectId,
        EntityId contextId,
        IReadOnlyList<TomeSection> sections)
    {
        Kind = kind;
        SubjectId = subjectId;
        ContextId = contextId;
        Sections = sections;
    }

    public TomeContentKind Kind { get; }

    public EntityId SubjectId { get; }

    public EntityId ContextId { get; }

    public IReadOnlyList<TomeSection> Sections { get; }

    /// <summary>Maximum number of additional settlement copies this work may produce.</summary>
    public int CopyLimit { get; internal set; }

    /// <summary>Copies made from this work, in chronological order.</summary>
    public List<TomeCopy> Copies { get; } = new();

    internal void CopyTo(int year, EntityId settlementId, EntityId sourceSettlementId)
    {
        if (Copies.Count >= CopyLimit)
        {
            throw new InvalidOperationException("A tome cannot exceed its circulation limit.");
        }

        foreach (TomeCopy copy in Copies)
        {
            if (copy.SettlementId == settlementId)
            {
                throw new InvalidOperationException("A settlement cannot receive the same tome twice.");
            }
        }

        Copies.Add(new TomeCopy(year, settlementId, sourceSettlementId));
    }
}

public static class ArtifactKinds
{
    /// <summary>
    /// Nouns an artifact's name can be built on, per kind.
    /// </summary>
    /// <remarks>
    /// Several per kind rather than one, chosen by the artifact's own id. Naming is a pure
    /// function of identity here as it is everywhere else — nothing consults what has been named
    /// already — so the only defence against two relics out of the same shrine being called the
    /// same thing is to have more than one word for a relic. It is also simply better writing:
    /// a world with a Shroud, a Reliquary and a Censer in it reads richer than one with three
    /// Relics.
    /// </remarks>
    private static readonly string[][] Nouns =
    {
        new[] { "Crown", "Diadem", "Sceptre", "Throne" },       // Regalia
        new[] { "Sword", "Spear", "Axe", "Blade" },             // Weapon
        new[] { "Relic", "Shroud", "Reliquary", "Censer" },     // Relic
        new[] { "Book", "Codex", "Chronicle", "Testament" },    // Tome
        new[] { "Idol", "Effigy", "Statue", "Icon" },           // Idol
        new[] { "Jewel", "Circlet", "Torc", "Ring" },           // Jewel
        new[] { "Clothing", "Garment", "Garb", "Apparel" },     // Clothing
        new[] { "Armor", "Plate", "Hauberk", "Cuirass" },       // Armor
    };

    /// <summary>The noun this artifact's name is built on: "the <b>Shroud</b> of Aigionanvos".</summary>
    public static string Noun(ArtifactKind kind, int discriminator)
    {
        int index = (int)kind;
        if (index < 0 || index >= Nouns.Length) return "Treasure";

        string[] choices = Nouns[index];

        // Non-negative regardless of the discriminator's sign, so the mapping never throws on an
        // id that arrives negative.
        return choices[Math.Abs(discriminator) % choices.Length];
    }

    public static string Label(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Regalia => "regalia",
        ArtifactKind.Weapon => "a weapon",
        ArtifactKind.Relic => "a relic",
        ArtifactKind.Tome => "a book",
        ArtifactKind.Idol => "an idol",
        ArtifactKind.Jewel => "a jewel",
        ArtifactKind.Clothing => "clothing",
        ArtifactKind.Armor => "armor",
        _ => "a treasure",
    };
}

/// <summary>Where an artifact was, from a given year, and how it got there.</summary>
public sealed record ArtifactHolding(int Year, EntityId SettlementId, string How);

/// <summary>
/// A made thing that outlives whoever made it.
/// </summary>
/// <remarks>
/// <para><b>Artifacts are held by settlements, not by people.</b> A crown carried by a king is the
/// more romantic model and a worse one: every death would need a rule for where the thing went,
/// and most of those rules would be arbitrary. A treasury sits in a place, and that place can be
/// sacked, abandoned or ceded — which is what makes an artifact a way of reading the map's
/// history rather than an inventory line. The person who made it is recorded and does not have
/// to still be alive for the object to matter.</para>
///
/// <para><b>Provenance is the point.</b> Every move is appended, never overwritten, so an object
/// that was made in one realm, looted into a second and lost when a third burned the place down
/// carries all three facts — which is a great deal of history in one page.</para>
/// </remarks>
public sealed class Artifact
{
    public Artifact(
        EntityId id,
        string name,
        ArtifactKind kind,
        EntityId originSettlementId,
        int createdYear)
    {
        Id = id;
        Name = name;
        Kind = kind;
        OriginSettlementId = originSettlementId;
        CreatedYear = createdYear;
        HolderId = originSettlementId;
        Provenance = new List<ArtifactHolding> { new(createdYear, originSettlementId, "where it was made") };
    }

    public EntityId Id { get; }

    public string Name { get; }

    public ArtifactKind Kind { get; }

    /// <summary>Whoever commissioned or made it, if the chronicle knows.</summary>
    public EntityId CreatorId { get; set; } = EntityId.None;

    public EntityId OriginSettlementId { get; }

    /// <summary>The faith it is sacred to, for relics and idols.</summary>
    public EntityId ReligionId { get; set; } = EntityId.None;

    /// <summary>The written account, for books, codices, chronicles and testaments.</summary>
    public TomeContents? TomeContents { get; set; }

    public int CreatedYear { get; }

    /// <summary>The settlement holding it now. <see cref="EntityId.None"/> once it is lost.</summary>
    public EntityId HolderId { get; set; }

    public int? LostYear { get; set; }

    public bool IsExtant => LostYear is null;

    /// <summary>Everywhere it has been, oldest first.</summary>
    public List<ArtifactHolding> Provenance { get; }

    public void MoveTo(EntityId settlementId, int year, string how)
    {
        HolderId = settlementId;
        Provenance.Add(new ArtifactHolding(year, settlementId, how));
    }

    public void Lose(int year, string how)
    {
        HolderId = EntityId.None;
        LostYear = year;
        Provenance.Add(new ArtifactHolding(year, EntityId.None, how));
    }

    public override string ToString() => $"{Id} {Name}";
}
