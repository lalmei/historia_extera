namespace HistoryEngine.Entities;

/// <summary>
/// The trade a guildsman actually practises. Explicit values — part of the export format.
/// </summary>
/// <remarks>
/// <para>Beside <see cref="Occupation.Guild"/> rather than inside it. Occupation is what the offices
/// read — <see cref="World.Occupations.ForOffice"/> and <see cref="World.Occupations.Affinity"/> map
/// careers to seats, and <see cref="World.Upbringings"/> buckets them into families — and a mason
/// and a smith are both what a guild-master's seat is looking for. Splitting the occupation would
/// have fragmented every one of those reads to say nothing new.</para>
///
/// <para>What a craft adds is the half the record was missing: a guildsman who can be told from
/// another guildsman. Which one somebody practises is decided by the place they live in, because
/// every craft here needs something from the ground, the water or the road — see
/// <see cref="World.Crafts.Supports"/>, which is the one place that says what.</para>
/// </remarks>
public enum Craft
{
    /// <summary>Not a guildsman, or not yet asked.</summary>
    None = 0,

    /// <summary>Iron. The one craft an ordinary village can hold on its own.</summary>
    Smith = 1,

    /// <summary>Harness and plate. A separate guild from the smith, and a town's trade.</summary>
    Armourer = 2,

    /// <summary>Gold and silver. Where the wealth is, not where the metal is.</summary>
    Goldsmith = 3,

    /// <summary>Stone. The craft whose work is a place rather than an object.</summary>
    Mason = 4,

    /// <summary>Timber. Everything in a town that is not stone.</summary>
    Carpenter = 5,

    /// <summary>Hulls. Sheltered water and timber, which rarely coincide.</summary>
    Shipwright = 6,

    /// <summary>The sea itself. A trade in the sense that it is entered and lived in.</summary>
    Sailor = 7,

    /// <summary>Cloth, from a hinterland that grows wool or flax.</summary>
    Weaver = 8,

    /// <summary>Colour. Running water, and a route for anything beyond the local plants.</summary>
    Dyer = 9,

    /// <summary>Hides. Wanted by every town and wanted downstream of all of them.</summary>
    Tanner = 10,

    /// <summary>Clay. The commonest made thing there is.</summary>
    Potter = 11,

    /// <summary>Fuel. The craft the fire-using trades stand on.</summary>
    CharcoalBurner = 12,

    /// <summary>Glass. Sand is everywhere and the craft was not.</summary>
    Glassblower = 13,

    /// <summary>Barrels. What everything that moved in bulk moved in.</summary>
    Cooper = 14,

    /// <summary>Wheels and carts, which need a carpenter and a smith at once.</summary>
    Wheelwright = 15,

    /// <summary>Grain into flour, by falling water.</summary>
    Miller = 16,

    /// <summary>Bread. The most numerous urban trade there was.</summary>
    Baker = 17,

    /// <summary>Ale. Grain, water, fuel and barrels — all four or none.</summary>
    Brewer = 18,

    /// <summary>Salt, from pans or from springs. Rare ground, and everyone needs it.</summary>
    Salter = 19,

    /// <summary>The making of books, as against the writing of them.</summary>
    Bookbinder = 20,

    /// <summary>Remedies. Present at the deathbed, changing nothing, recorded anyway.</summary>
    Apothecary = 21,
}
