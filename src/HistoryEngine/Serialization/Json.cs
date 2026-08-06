using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>Entity ids are used as dictionary keys in the export's indices.</summary>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString());

    public override EntityId ReadAsPropertyName(
        ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        EntityId.Parse(reader.GetString() ?? string.Empty);
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

        return options;
    }
}
