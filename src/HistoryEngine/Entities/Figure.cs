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
}

/// <summary>A title held over a span of years.</summary>
public sealed record TitleHolding(string Title, EntityId CivilizationId, int FromYear, int? ToYear);

/// <summary>
/// A notable person.
/// </summary>
/// <remarks>
/// Only leaders and a handful of notables per civilization are simulated — the brief's
/// deliberate choice, and the reason a few centuries can run in seconds. Ordinary people
/// exist only as <see cref="Settlement.Population"/>.
///
/// <para>Figures are never deleted on death; they gain a <see cref="DeathYear"/>. A dead
/// king is exactly the sort of entity the viewer needs to keep resolving, since most of the
/// events referencing him are written after he dies.</para>
/// </remarks>
public sealed class Figure
{
    public Figure(
        EntityId id,
        EntityId civilizationId,
        EntityId cultureId,
        string name,
        int birthYear)
    {
        Id = id;
        CivilizationId = civilizationId;
        CultureId = cultureId;
        Name = name;
        BirthYear = birthYear;
        Titles = new List<TitleHolding>();
        ParentIds = new List<EntityId>();
        SpouseIds = new List<EntityId>();
    }

    public EntityId Id { get; }

    public EntityId CivilizationId { get; set; }

    public EntityId CultureId { get; }

    public string Name { get; set; }

    public int BirthYear { get; }

    public int? DeathYear { get; set; }

    public DeathCause DeathCause { get; set; } = DeathCause.Unknown;

    public bool IsAlive => DeathYear is null;

    /// <summary>Reserved for Milestone 5. <see cref="EntityId.None"/> until dynasties land.</summary>
    public EntityId DynastyId { get; set; } = EntityId.None;

    public List<TitleHolding> Titles { get; }

    public List<EntityId> ParentIds { get; }

    public List<EntityId> SpouseIds { get; }

    /// <summary>Where this figure was born, if known.</summary>
    public EntityId BirthSettlementId { get; set; } = EntityId.None;

    public int AgeIn(int year) => year - BirthYear;

    public int? AgeAtDeath => DeathYear is null ? null : DeathYear.Value - BirthYear;

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

    /// <summary>Closes any open title holding as of <paramref name="year"/>.</summary>
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

    public override string ToString() =>
        $"{Id} {Name} ({BirthYear}–{(DeathYear?.ToString() ?? string.Empty)})";
}
