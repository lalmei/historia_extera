namespace HistoryEngine.Entities;

/// <summary>
/// What a settlement's ground was chosen for. Explicit values — part of the export format.
/// </summary>
/// <remarks>
/// One categorical value rather than the vector of measures that produced it, for the same reason
/// a chronicle records that a city stands where two rivers meet rather than recording six weights:
/// the numbers decide, and the category is the part history refers to afterwards.
/// </remarks>
public enum SiteCharacter
{
    /// <summary>Unremarkable ground, taken for its soil.</summary>
    Plain = 0,

    /// <summary>On a river.</summary>
    Riverside = 1,

    /// <summary>Where two rivers meet.</summary>
    Confluence = 2,

    /// <summary>Where a river reaches the sea.</summary>
    Estuary = 3,

    /// <summary>Sheltered water — a place to bring ships into.</summary>
    Harbour = 4,

    /// <summary>Open shore.</summary>
    Coastal = 5,

    /// <summary>The low way through high country.</summary>
    Pass = 6,

    // There is no value for defensible high ground. M10 set out to add one and measured four
    // formulations of it, each of which put more settlements on unbuildable ground than the label
    // was worth — see SiteSelection.SteepestGrade.
}

public static class SiteCharacters
{
    public static string Label(SiteCharacter character) => character switch
    {
        SiteCharacter.Riverside => "on the river",
        SiteCharacter.Confluence => "at the meeting of two rivers",
        SiteCharacter.Estuary => "where the river meets the sea",
        SiteCharacter.Harbour => "on sheltered water",
        SiteCharacter.Coastal => "on the coast",
        SiteCharacter.Pass => "astride the pass",
        _ => "on open ground",
    };
}
