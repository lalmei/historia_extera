using HistoryEngine.Core;

namespace HistoryEngine.Events;

/// <summary>Where systems write history.</summary>
public interface IChronicle
{
    int Count { get; }

    HistoryEvent Record(
        int year,
        EventKind kind,
        EntityId subject,
        EntityId obj = default,
        EntityId location = default,
        IReadOnlyList<EntityId>? extra = null,
        DetMap<string, string>? data = null);
}

/// <summary>
/// The append-only event log for one run.
/// </summary>
/// <remarks>
/// Ids are assigned sequentially on append, so an event's id encodes its position in the
/// log. That makes the export's indices plain integer arrays into the event list rather than
/// lookups, which is what keeps the viewer's entity pages instant at fifty thousand events.
///
/// <para>Append-only is a real constraint, not just a description: no system may revise or
/// delete an event once written. If a war's outcome is not known until it ends, the ending
/// is a new event, not an edit to the declaration. Otherwise the log stops being a record of
/// what happened and becomes a mutable summary of current state.</para>
/// </remarks>
public sealed class Chronicle : IChronicle
{
    private readonly List<HistoryEvent> _events = new();

    public int Count => _events.Count;

    public IReadOnlyList<HistoryEvent> Events => _events;

    public HistoryEvent Record(
        int year,
        EventKind kind,
        EntityId subject,
        EntityId obj = default,
        EntityId location = default,
        IReadOnlyList<EntityId>? extra = null,
        DetMap<string, string>? data = null)
    {
        var entry = new HistoryEvent(
            Id: _events.Count,
            Year: year,
            Kind: kind,
            Subject: subject,
            Object: obj,
            Location: location,
            Extra: extra,
            Data: data);

        _events.Add(entry);
        return entry;
    }

    /// <summary>Convenience for building a small display payload without ceremony at call sites.</summary>
    public static DetMap<string, string> Data(params (string Key, string Value)[] pairs)
    {
        var map = new DetMap<string, string>();
        foreach ((string key, string value) in pairs)
        {
            map[key] = value;
        }

        return map;
    }
}
