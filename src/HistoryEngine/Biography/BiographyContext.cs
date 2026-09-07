using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Biography;

/// <summary>Everything needed to interpret and render one life at a point in time.</summary>
public sealed class BiographyContext
{
    public BiographyContext(
        Figure figure,
        int year,
        Func<EntityId, string> nameOf,
        Func<EntityId, Sex?>? sexOf = null,
        IReadOnlyList<int>? occupationTakenYears = null)
    {
        Figure = figure;
        Year = year;
        NameOf = nameOf;
        SexOf = sexOf ?? (_ => null);
        OccupationTakenYears = occupationTakenYears ?? Array.Empty<int>();
    }

    public Figure Figure { get; }

    /// <summary>The year the biography is read at — death year if already dead.</summary>
    public int Year { get; }

    public Func<EntityId, string> NameOf { get; }

    public Func<EntityId, Sex?> SexOf { get; }

    /// <summary>
    /// Years this figure took an occupation, from the chronicle. Empty when the caller
    /// has only figure state — extraction then falls back to birth year.
    /// </summary>
    public IReadOnlyList<int> OccupationTakenYears { get; }

    public string Name => Figure.Name;

    public Sex Sex => Figure.Sex;

    public Disposition Disposition => Figure.Disposition;

    public string Possessive => Figure.Sex switch
    {
        Sex.Female => "her",
        Sex.Male => "his",
        _ => "their",
    };

    public string Subject => Figure.Sex switch
    {
        Sex.Female => "She",
        Sex.Male => "He",
        _ => "They",
    };

    public string Object => Figure.Sex switch
    {
        Sex.Female => "her",
        Sex.Male => "him",
        _ => "them",
    };

    public string PlaceName(EntityId placeId) =>
        placeId.IsNone ? "an unrecorded place" : NameOf(placeId);

    public string FigureName(EntityId figureId) =>
        figureId.IsNone ? "someone unnamed" : NameOf(figureId);

    public double Dial(BiographyDial dial) => dial switch
    {
        BiographyDial.Aggression => Disposition.Values.Aggression,
        BiographyDial.Expansionism => Disposition.Values.Expansionism,
        BiographyDial.Piety => Disposition.Values.Piety,
        BiographyDial.Tradition => Disposition.Values.Tradition,
        BiographyDial.Mercantile => Disposition.Values.Mercantile,
        BiographyDial.Learning => Disposition.Values.Learning,
        BiographyDial.Centralism => Disposition.Centralism,
        BiographyDial.Independence => Disposition.Independence,
        _ => 0.5,
    };

    public static int StandingYear(Figure figure, int requestedYear) =>
        figure.DeathYear is int death && death <= requestedYear ? death : requestedYear;
}
