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

    /// <summary>A fortress on a defensible position.</summary>
    Fortress = 7,

    /// <summary>A mine site.</summary>
    Mine = 8,

    /// <summary>Within a trading route.</summary>
    TradeRoute = 9,

    /// <summary>A Holy Site.</summary>
    HolySite = 10,

    /// <summary>A quarry site.</summary>
    Quarry = 11,

    /// <summary>A strategic position.</summary>
    Strategic = 12,

    // Mine is produced, by a party sent out for the deposit rather than for the soil — see
    // FoundingNeed and SiteSelection.Characterise. The remaining values above Pass are declared
    // ahead of the selection that would produce them: no settlement carries one of them yet and no
    // reader should expect to see one.
    //
    // Fortress in particular is a name, not a decision that has been made: M10 set out to select
    // for defensible high ground and measured four formulations of it, each of which put more
    // settlements on unbuildable ground than the label was worth — see SiteSelection.SteepestGrade.
    // Whatever eventually fills Fortress has to answer that measurement rather than repeat it.
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
        SiteCharacter.Fortress => "on a defensible position",
        SiteCharacter.Mine => "by a mine site",
        SiteCharacter.TradeRoute => "within a trading route",
        SiteCharacter.HolySite => "a holy site",
        SiteCharacter.Quarry => "a quarry site",
        SiteCharacter.Strategic => "a strategic position",
        _ => "on open ground",
    };

    /// <summary>
    /// Why a party was sent to ground of this character, where being sent is what put them there.
    /// </summary>
    /// <remarks>
    /// <para>Null for every character a search arrives at rather than sets out for, which is most
    /// of them: a town is not founded <em>in order to</em> be beside a river, it is founded where
    /// people were going anyway and the river is why that spot won. Only the characters a
    /// <see cref="World.FoundingNeed"/> produces have an errand behind them worth recording, and
    /// the chronicle drops the clause for the rest rather than inventing a motive for ordinary
    /// colonisation.</para>
    ///
    /// <para>A clause rather than a sentence, because it is substituted into the founding
    /// template beside the settlers and where they came from.</para>
    /// </remarks>
    public static string? Purpose(SiteCharacter character) => character switch
    {
        SiteCharacter.Mine => "to work the ore",
        _ => null,
    };
}
