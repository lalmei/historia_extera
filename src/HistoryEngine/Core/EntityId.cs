using System.Diagnostics;
using System.Globalization;

namespace HistoryEngine.Core;

/// <summary>
/// A reference to one entity, as a (kind, index) pair.
/// </summary>
/// <remarks>
/// Serialises as <c>"civ:3"</c> rather than an opaque integer or a GUID. That costs a
/// few bytes per reference and buys three things that matter more: the exported JSON
/// stays greppable when a history looks wrong, the viewer's URLs are readable
/// (<c>#/fig/1204</c>), and a mistyped cross-reference fails loudly as a bad kind
/// instead of silently resolving to some unrelated entity.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public readonly record struct EntityId(EntityKind Kind, int Index) : IComparable<EntityId>
{
    public static readonly EntityId None = new(EntityKind.None, -1);

    public bool IsNone => Kind == EntityKind.None;

    public static EntityId Culture(int i) => new(EntityKind.Culture, i);

    public static EntityId Civilization(int i) => new(EntityKind.Civilization, i);

    public static EntityId Settlement(int i) => new(EntityKind.Settlement, i);

    public static EntityId Figure(int i) => new(EntityKind.Figure, i);

    public static EntityId Dynasty(int i) => new(EntityKind.Dynasty, i);

    public static EntityId War(int i) => new(EntityKind.War, i);

    public static EntityId Battle(int i) => new(EntityKind.Battle, i);

    public static EntityId Region(int i) => new(EntityKind.Region, i);

    public override string ToString() =>
        IsNone ? "none" : Kind.Prefix() + ":" + Index.ToString(CultureInfo.InvariantCulture);

    public static EntityId Parse(string text) =>
        TryParse(text, out EntityId id)
            ? id
            : throw new FormatException($"'{text}' is not a valid entity id.");

    public static bool TryParse(string? text, out EntityId id)
    {
        id = None;

        if (string.IsNullOrEmpty(text)) return false;
        if (string.Equals(text, "none", StringComparison.Ordinal)) return true;

        int colon = text!.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) return false;

        if (!EntityKindExtensions.TryParsePrefix(text.Substring(0, colon), out EntityKind kind))
        {
            return false;
        }

        if (!int.TryParse(
                text.Substring(colon + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int index))
        {
            return false;
        }

        id = new EntityId(kind, index);
        return true;
    }

    /// <summary>
    /// Total order over ids: by kind, then index. Gives collections keyed by
    /// <see cref="EntityId"/> a stable iteration order without a culture-sensitive
    /// string comparison in the hot path.
    /// </summary>
    public int CompareTo(EntityId other)
    {
        int byKind = ((int)Kind).CompareTo((int)other.Kind);
        return byKind != 0 ? byKind : Index.CompareTo(other.Index);
    }

    /// <summary>A stable 64-bit value for seeding an RNG substream about this entity.</summary>
    public long ToDiscriminator() => ((long)Kind << 32) | (uint)Index;
}
