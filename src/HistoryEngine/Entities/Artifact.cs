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
    Cosmology = 6,
    Dedication = 7,
    RealmChronicle = 8,
    Itinerary = 9,
}

/// <summary>One passage in a tome, with the entities a reader can follow from it.</summary>
/// <param name="Year">
/// When this passage was written. Later continuations keep the original sections and add new
/// ones dated to the year they were entered, so a chronicle can be updated without rewriting
/// what an earlier scribe put down.
/// </param>
public sealed record TomeSection(
    string Heading,
    string Text,
    IReadOnlyList<EntityId> References,
    int Year = 0);

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
        IReadOnlyList<TomeSection> sections,
        int year = 0)
    {
        Kind = kind;
        SubjectId = subjectId;
        ContextId = contextId;

        var stamped = new List<TomeSection>(sections.Count);
        foreach (TomeSection section in sections)
        {
            stamped.Add(section.Year == 0 && year > 0 ? section with { Year = year } : section);
        }

        Sections = stamped;
    }

    public TomeContentKind Kind { get; }

    public EntityId SubjectId { get; }

    public EntityId ContextId { get; }

    public List<TomeSection> Sections { get; }

    /// <summary>The latest year any passage in this work was entered.</summary>
    public int LatestYear
    {
        get
        {
            int latest = 0;
            foreach (TomeSection section in Sections)
            {
                if (section.Year > latest) latest = section.Year;
            }

            return latest;
        }
    }

    internal void Continue(TomeSection section)
    {
        if (section.Year == 0)
        {
            throw new InvalidOperationException("A continuation must be dated.");
        }

        Sections.Add(section);
    }

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

/// <summary>Where an artifact was, who claimed it, from a given year, and how it got there.</summary>
public sealed record ArtifactHolding(int Year, EntityId SettlementId, EntityId OwnerId, string How);

/// <summary>
/// A made thing that outlives whoever made it.
/// </summary>
/// <remarks>
/// <para><b>A thing sits in a place and belongs to a person.</b> The settlement is where it can
/// be sacked, abandoned or copied; the owner is who can inherit it, gift it, or commission its
/// like. Treating those as one id made succession arbitrary and left famous objects as inventory
/// lines on towns. They travel together when a court moves and come apart when a treasury keeps
/// what a dead owner no longer claims.</para>
///
/// <para><b>Provenance is the point.</b> Every change of place or owner is appended, never
/// overwritten, so an object made for one ruler, inherited by a second and looted into a third
/// realm carries all three facts — which is a great deal of history in one page.</para>
/// </remarks>
public sealed class Artifact
{
    public Artifact(
        EntityId id,
        string name,
        ArtifactKind kind,
        EntityId originSettlementId,
        int createdYear,
        EntityId ownerId = default)
    {
        Id = id;
        Name = name;
        Kind = kind;
        OriginSettlementId = originSettlementId;
        CreatedYear = createdYear;
        HolderId = originSettlementId;
        OwnerId = ownerId;
        Provenance = new List<ArtifactHolding>
        {
            new(createdYear, originSettlementId, ownerId, "where it was made"),
        };
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

    /// <summary>The settlement keeping it now. <see cref="EntityId.None"/> once it is lost.</summary>
    public EntityId HolderId { get; set; }

    /// <summary>
    /// The living person who claims it, or none while it sits in a treasury.
    /// </summary>
    public EntityId OwnerId { get; set; }

    public int? LostYear { get; set; }

    public bool IsExtant => LostYear is null;

    /// <summary>Everywhere it has been, and who claimed it, oldest first.</summary>
    public List<ArtifactHolding> Provenance { get; }

    /// <summary>
    /// Records a change of place, owner, or both. A no-op is refused so provenance never
    /// contains a journey the object did not make.
    /// </summary>
    public void Transfer(EntityId settlementId, EntityId ownerId, int year, string how)
    {
        if (HolderId == settlementId && OwnerId == ownerId)
        {
            throw new InvalidOperationException("An artifact cannot arrive where its owner already keeps it.");
        }

        HolderId = settlementId;
        OwnerId = ownerId;
        Provenance.Add(new ArtifactHolding(year, settlementId, ownerId, how));
    }

    public void MoveTo(EntityId settlementId, int year, string how) =>
        Transfer(settlementId, OwnerId, year, how);

    public void Lose(int year, string how)
    {
        HolderId = EntityId.None;
        OwnerId = EntityId.None;
        LostYear = year;
        Provenance.Add(new ArtifactHolding(year, EntityId.None, EntityId.None, how));
    }

    public override string ToString() => $"{Id} {Name}";
}
