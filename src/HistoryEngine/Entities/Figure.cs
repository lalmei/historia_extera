using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>How a notable figure's life ended. Explicit values — part of the export format.</summary>
public enum DeathCause
{
    Unknown = 0,
    OldAge = 1,
    Illness = 2,
    Battle = 3,
    Assassination = 4,
    Accident = 5,
    Execution = 6,
    Childbirth = 7,
}

/// <summary>
/// Which half of a marriage a figure can be.
/// </summary>
/// <remarks>
/// Modelled because succession law is the sharpest way one culture differs from another in a
/// chronicle, and every historical succession law is phrased in terms of sex — agnatic, male
/// preference, absolute. Without it there is one inheritance rule and every realm follows it.
/// </remarks>
public enum Sex
{
    Female = 0,
    Male = 1,
}

/// <summary>A title held over a span of years.</summary>
public sealed record TitleHolding(string Title, EntityId CivilizationId, int FromYear, int? ToYear);

/// <summary>
/// A notable person.
/// </summary>
/// <remarks>
/// Only leaders and their houses are simulated — the brief's deliberate choice, and the reason
/// a few centuries can run in seconds. Ordinary people exist only as
/// <see cref="Settlement.Population"/>.
///
/// <para>Figures are never deleted on death; they gain a <see cref="DeathYear"/>. A dead
/// king is exactly the sort of entity the viewer needs to keep resolving, since most of the
/// events referencing him are written after he dies.</para>
///
/// <para><b>Parents are named, not listed.</b> <see cref="MotherId"/> and
/// <see cref="FatherId"/> rather than a two-element collection, because inheritance asks
/// which parent specifically: a house passes down the father's line unless there is no
/// father's house to pass, and a list would force every caller to guess which end is which.</para>
/// </remarks>
public sealed class Figure
{
    public Figure(
        EntityId id,
        EntityId civilizationId,
        EntityId cultureId,
        string name,
        Sex sex,
        int birthYear)
    {
        Id = id;
        CivilizationId = civilizationId;
        CultureId = cultureId;
        Name = name;
        Sex = sex;
        BirthYear = birthYear;
        Titles = new List<TitleHolding>();
        ChildIds = new List<EntityId>();
        SpouseIds = new List<EntityId>();
    }

    public EntityId Id { get; }

    /// <summary>
    /// The realm this figure lives in.
    /// </summary>
    /// <remarks>Mutable: marrying abroad moves one partner, and inheriting a throne brings them home.</remarks>
    public EntityId CivilizationId { get; set; }

    public EntityId CultureId { get; }

    /// <summary>The personal name. See <see cref="FullName"/> for how a ruler is styled.</summary>
    public string Name { get; set; }

    /// <summary>
    /// Which holder of this name in this realm, or zero for someone who needs no distinguishing.
    /// </summary>
    /// <remarks>
    /// <para>Milestone 5 took a world from eighty named people to nearly a thousand, and names are a
    /// pure function of entity id with no uniqueness guarantee — so within a single culture the
    /// same name now recurs constantly. In a long run nearly half of all reigns belong to a realm
    /// that has had another ruler of the same name, which makes a line of succession genuinely
    /// unreadable.</para>
    ///
    /// <para>The fix is the one every real chronicle uses. Numbering is assigned at accession from
    /// the realm's own list of past rulers, so it depends on who ruled rather than on the order
    /// names were requested — the property <see cref="Naming.INameGenerator"/> exists to protect.
    /// The first of a name is numbered retroactively when the second appears, exactly as
    /// historians do it, which costs nothing because events carry ids and resolve names when they
    /// are rendered.</para>
    /// </remarks>
    public int RegnalNumber { get; set; }

    /// <summary>How the chronicle styles this figure: the name, with a numeral if one is needed.</summary>
    public string FullName => RegnalNumber >= 2 ? Name + " " + Numeral(RegnalNumber) : Name;

    public Sex Sex { get; }

    public int BirthYear { get; }

    public int? DeathYear { get; set; }

    public DeathCause DeathCause { get; set; } = DeathCause.Unknown;

    public bool IsAlive => DeathYear is null;

    /// <summary>
    /// The house this figure belongs to by blood, or <see cref="EntityId.None"/> for someone
    /// married in from outside the recorded houses.
    /// </summary>
    /// <remarks>
    /// Blood membership, not household membership — a consort keeps whatever house they were born
    /// into. That distinction is the whole basis of succession: only a house's blood can inherit
    /// its claims, which is what makes a house able to die out while its widows live on.
    /// </remarks>
    public EntityId DynastyId { get; set; } = EntityId.None;

    public List<TitleHolding> Titles { get; }

    public EntityId MotherId { get; set; } = EntityId.None;

    public EntityId FatherId { get; set; } = EntityId.None;

    /// <summary>Children, in birth order — appended as they are born.</summary>
    public List<EntityId> ChildIds { get; }

    /// <summary>Every marriage, in order. A widowed figure who remarries keeps both.</summary>
    public List<EntityId> SpouseIds { get; }

    /// <summary>The living spouse, if any. Cleared when a marriage ends in death.</summary>
    public EntityId SpouseId { get; set; } = EntityId.None;

    public bool IsMarried => !SpouseId.IsNone;

    /// <summary>Where this figure was born, if known.</summary>
    public EntityId BirthSettlementId { get; set; } = EntityId.None;

    public int AgeIn(int year) => year - BirthYear;

    public int? AgeAtDeath => DeathYear is null ? null : DeathYear.Value - BirthYear;

    /// <summary>Both parents, mother first, skipping any that are unrecorded.</summary>
    public IEnumerable<EntityId> Parents()
    {
        if (!MotherId.IsNone) yield return MotherId;
        if (!FatherId.IsNone) yield return FatherId;
    }

    /// <summary>The title currently held, if any.</summary>
    public TitleHolding? CurrentTitle
    {
        get
        {
            // Reverse order: the most recently granted title is the operative one.
            for (int i = Titles.Count - 1; i >= 0; i--)
            {
                if (Titles[i].ToYear is null) return Titles[i];
            }

            return null;
        }
    }

    /// <summary>Closes the most recently granted open title as of <paramref name="year"/>.</summary>
    /// <remarks>
    /// For laying down one office while keeping another — a term expiring, a regency ending. Use
    /// <see cref="EndAllTitles"/> when the holder is what ended.
    /// </remarks>
    public void EndCurrentTitle(int year)
    {
        for (int i = Titles.Count - 1; i >= 0; i--)
        {
            if (Titles[i].ToYear is null)
            {
                Titles[i] = Titles[i] with { ToYear = year };
                return;
            }
        }
    }

    /// <summary>
    /// Closes every open title as of <paramref name="year"/>.
    /// </summary>
    /// <remarks>
    /// A figure can hold two offices at once — a regency for one realm and a throne of their own —
    /// and death ends both. Closing only the most recent left the older one open for ever, which
    /// surfaced as a regent still recorded as governing three centuries after they died. It took
    /// M8's plague to find: before it, the deaths that reached a double office-holder were rare
    /// enough not to occur in any tested seed.
    /// </remarks>
    public void EndAllTitles(int year)
    {
        for (int i = 0; i < Titles.Count; i++)
        {
            if (Titles[i].ToYear is null) Titles[i] = Titles[i] with { ToYear = year };
        }
    }

    /// <summary>Roman numerals, for regnal numbering.</summary>
    /// <remarks>
    /// Written out rather than table-indexed by a small cap, because "the fifteenth ruler of this
    /// name" is not a case anyone will think to test until a nine-hundred-year run produces one.
    /// </remarks>
    private static string Numeral(int value)
    {
        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        var text = new System.Text.StringBuilder(8);
        int remaining = value;

        for (int i = 0; i < values.Length && remaining > 0; i++)
        {
            while (remaining >= values[i])
            {
                text.Append(symbols[i]);
                remaining -= values[i];
            }
        }

        return text.ToString();
    }

    public override string ToString() =>
        $"{Id} {FullName} ({BirthYear}–{(DeathYear?.ToString() ?? string.Empty)})";
}
