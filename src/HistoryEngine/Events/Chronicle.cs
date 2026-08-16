using System.Globalization;
using HistoryEngine.Core;

namespace HistoryEngine.Events;

/// <summary>Where systems write history.</summary>
public interface IChronicle
{
    int Count { get; }

    /// <summary>
    /// Declares which step is now running, so events written in it can be dated.
    /// </summary>
    /// <remarks>
    /// Called by the simulator, never by a system. It is what lets <see cref="Record"/> keep the
    /// signature several hundred call sites already use while events gain a day: a system says what
    /// happened and in which year, and the step it is running in says when within that year. The
    /// alternative — threading a <see cref="Stamp"/> through every recording call — would make the
    /// day a parameter that every caller has to get right, when only one caller in the engine
    /// actually knows it.
    /// </remarks>
    void OpenStep(Stamp now);

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

    /// <summary>The step currently running, or the opening of year zero before one has been.</summary>
    /// <remarks>
    /// Not part of the world's state and never exported: it is scaffolding for the writer, and a
    /// run that resumed mid-year would set it from the step it resumed into rather than restore it.
    /// </remarks>
    public Stamp Now { get; private set; }

    public void OpenStep(Stamp now) => Now = now;

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
            Data: data)
        {
            // The open step dates the event, but only where the caller is writing about the year it
            // is standing in. A year named that is not the open one is the world being built before
            // any step has run, or a system reaching outside the step it belongs to — and neither
            // has a day to offer, so both get the opening of the year they named rather than a day
            // borrowed from a different one.
            Day = Now.Year == year ? Now.Day : 0,
        };

        _events.Add(entry);
        return entry;
    }

    /// <summary>
    /// A span of years as prose, pluralised.
    /// </summary>
    /// <remarks>
    /// Pluralised at the point the payload is built, because the template grammar has optional
    /// segments but deliberately no conditionals — and "a child of 1 years" is not reason enough
    /// to give it any.
    /// </remarks>
    public static string Years(int count) =>
        count == 1 ? "1 year" : count.ToString(CultureInfo.InvariantCulture) + " years";

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
