using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HistoryEngine.Core;

namespace HistoryEngine.Serialization;

/// <summary>Serialises <see cref="EntityId"/> as its readable <c>"civ:3"</c> form.</summary>
public sealed class EntityIdJsonConverter : JsonConverter<EntityId>
{
    public override EntityId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        string? text = reader.GetString();
        return EntityId.TryParse(text, out EntityId id)
            ? id
            : throw new JsonException($"'{text}' is not a valid entity id.");
    }

    public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    /// <summary>
    /// Reads and writes an id used as a dictionary key.
    /// </summary>
    /// <remarks>
    /// Nothing in the export is keyed by an id since schema 57 dropped the derived indices, which
    /// were. Kept because a converter that cannot do this fails silently the first time a keyed
    /// dictionary is added — it writes the key through the default string handling instead.
    /// </remarks>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString());

    public override EntityId ReadAsPropertyName(
        ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        EntityId.Parse(reader.GetString() ?? string.Empty);
}

/// <summary>
/// Writes a double with the precision the value is actually read at.
/// </summary>
/// <remarks>
/// <para><see cref="System.Text.Json"/> writes a double at the shortest form that round-trips its
/// bits, which for anything derived by arithmetic is seventeen digits — <c>"piety":
/// 0.7269980808848671</c>. Nothing reads a disposition at seventeen digits; the viewer draws it as
/// a bar. On the standard world twenty-three thousand numbers were written that way, four hundred
/// kilobytes of digits nobody looks at.</para>
///
/// <para><b>Three decimals, extended for small magnitudes.</b> A flat three decimal places is
/// right for the 0–1 scales that are nearly all of these numbers, and wrong for the astronomy: a
/// moon of 0.0123 Earth masses would become 0.012, a two-percent error in a figure the sky view
/// draws. So values below 0.1 keep going until they have three significant digits. No value ever
/// moves by more than 0.0005, and below 0.1 the extra places hold the error under half a percent
/// of the value itself — measured across the standard world, the largest was 0.49%, on a dial of
/// 0.1015 written as 0.101.</para>
///
/// <para><b>Only the file rounds.</b> The simulation's own doubles are untouched — this converter
/// runs at serialisation and nowhere else, so rounding describes the export rather than changing
/// the history. <see cref="Utf8JsonWriter"/> writes invariant, and <see cref="Math.Round(double,
/// int, MidpointRounding)"/> is exact and platform-independent, so the golden fingerprint moves
/// once and then stays put.</para>
///
/// <para>Rounding is idempotent: reading a rounded file and writing it again produces the same
/// bytes, which is what <c>ExportTests.ExportRoundTripsLosslessly</c> asserts.</para>
/// </remarks>
public sealed class RoundedDoubleJsonConverter : JsonConverter<double>
{
    /// <summary>Below this magnitude, three decimals would cost significant digits.</summary>
    private const double SmallMagnitude = 0.1;

    private const int DecimalPlaces = 3;

    /// <summary>The most <see cref="Math.Round(double, int, MidpointRounding)"/> accepts.</summary>
    private const int MaxDecimalPlaces = 15;

    /// <summary>
    /// A magnitude below <c>Thresholds[n]</c> needs <c>3 + n + 1</c> places to keep three
    /// significant digits. Literals rather than a computed power, so the comparison is exact.
    /// </summary>
    private static readonly double[] Thresholds =
    {
        0.1, 0.01, 0.001, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8, 1e-9, 1e-10, 1e-11, 1e-12,
    };

    public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(Round(value));

    public static double Round(double value)
    {
        if (!double.IsFinite(value) || value == 0) return value;

        double magnitude = Math.Abs(value);
        if (magnitude >= SmallMagnitude) return Math.Round(value, DecimalPlaces, MidpointRounding.ToEven);

        // 0.0123 has one leading zero after the point, so four places keep three significant
        // digits. Counted against a table rather than with Math.Log10, which is not required to
        // return the same bits on every runtime and platform — and this count decides how the
        // file is written, so a disagreement would be a world file that differs by machine.
        int places = DecimalPlaces;
        while (places < MaxDecimalPlaces && magnitude < Thresholds[places - DecimalPlaces])
        {
            places++;
        }

        double rounded = Math.Round(value, places, MidpointRounding.ToEven);

        // Past the cap — a comet's mass in Earth masses is already 1e-10 — rounding would write
        // the value away entirely. A number too small to round is written as it is; seventeen
        // digits of something is still better than three of nothing.
        return rounded == 0 ? value : rounded;
    }
}

/// <summary>
/// The one set of serialiser options the export format uses.
/// </summary>
/// <remarks>
/// <para><b>Enums are written as strings.</b> Numeric enum values would be smaller, and would
/// silently change meaning the first time someone inserted a value into the middle of an enum.
/// Strings also mean the exported JSON can be read and grepped when a history looks wrong,
/// which is the main reason anyone opens a world file by hand.</para>
///
/// <para><b>Property order is declaration order.</b> System.Text.Json writes POCO properties in
/// the order they are declared, so the DTOs in <see cref="WorldExport"/> define the byte layout
/// of the file. That is what makes the golden-hash determinism test meaningful: identical seed
/// and config must produce a byte-identical file, so nothing in the pipeline may introduce
/// ordering that depends on runtime state.</para>
/// </remarks>
public static class Json
{
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    /// <summary>Indented, for reading by hand. Not the canonical form.</summary>
    public static JsonSerializerOptions Readable { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,

            // The export is machine-written and machine-read; no cycles, no reference handling.
            NumberHandling = JsonNumberHandling.Strict,
        };

        options.Converters.Add(new EntityIdJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new RoundedDoubleJsonConverter());

        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { OmitEmptyCollections },
        };

        return options;
    }

    /// <summary>
    /// Leaves an empty list or dictionary out of the file entirely.
    /// </summary>
    /// <remarks>
    /// <para>Most figures never went on campaign, never kept a plot and never held an office, and
    /// every one of them was carrying <c>"campaigns":[],"plots":[],"service":[]</c> — thirty-one
    /// distinct keys, twenty-five thousand of them on the standard world, for facts that did not
    /// happen. A missing container and an empty one say the same thing.</para>
    ///
    /// <para>This is only safe because every reader already treats an absent container as empty:
    /// the viewer's <c>normalizeExport</c> has filled missing lists since exports older than the
    /// current schema became readable, and it is the same code path. A reader that indexes into a
    /// container without going through it would now see <c>undefined</c> where it used to see an
    /// empty array, so adding a collection to the export means adding it there too.</para>
    ///
    /// <para>Strings are excluded deliberately — an empty name is a fact about a world, not an
    /// absent container — and so is anything that is not a collection.</para>
    /// </remarks>
    private static void OmitEmptyCollections(JsonTypeInfo info)
    {
        foreach (JsonPropertyInfo property in info.Properties)
        {
            if (property.PropertyType == typeof(string)) continue;
            if (!typeof(IEnumerable).IsAssignableFrom(property.PropertyType)) continue;

            Func<object, object?, bool>? already = property.ShouldSerialize;

            property.ShouldSerialize = (parent, value) =>
                !IsEmpty(value) && (already is null || already(parent, value));
        }
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        ICollection collection => collection.Count == 0,
        IEnumerable enumerable => !enumerable.GetEnumerator().MoveNext(),
        _ => false,
    };
}
