namespace HistoryEngine.Core;

/// <summary>
/// The kinds of thing history can be about.
/// </summary>
/// <remarks>
/// Values are explicit and must never be renumbered: they are part of the export
/// format via <see cref="EntityId"/>.
/// </remarks>
public enum EntityKind
{
    None = 0,
    Culture = 1,
    Civilization = 2,
    Settlement = 3,
    Figure = 4,
    Dynasty = 5,
    War = 6,
    Battle = 7,
    Region = 8,

    // Reserved for the Milestone 8 flavour systems. Declared now so the wire
    // numbering stays stable when they land.
    Artifact = 9,
    Religion = 10,
    TradeRoute = 11,
}

public static class EntityKindExtensions
{
    // Short, stable, greppable prefixes. These appear in every exported id and in
    // every viewer URL, so they are effectively public API.
    private static readonly string[] Prefixes =
    {
        "none", "cul", "civ", "set", "fig", "dyn", "war", "bat", "reg", "art", "rel", "rte",
    };

    public static string Prefix(this EntityKind kind)
    {
        int i = (int)kind;
        if (i < 0 || i >= Prefixes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entity kind.");
        }

        return Prefixes[i];
    }

    public static bool TryParsePrefix(string prefix, out EntityKind kind)
    {
        for (int i = 0; i < Prefixes.Length; i++)
        {
            if (string.Equals(Prefixes[i], prefix, StringComparison.Ordinal))
            {
                kind = (EntityKind)i;
                return true;
            }
        }

        kind = EntityKind.None;
        return false;
    }
}
