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

    /// <summary>Declares which system is writing, for the ordering <see cref="CloseStep"/> applies.</summary>
    void EnterSystem(int index);

    /// <summary>
    /// Dates what is written next, within the open step.
    /// </summary>
    /// <remarks>
    /// For scheduled work, which is the one thing in the engine that knows a finer day than the step
    /// it is resolved in: an episode due on day 40 is noticed at the step that opens on day 90 and
    /// is still recorded as having happened on day 40. The step is not reopened, so the entry sorts
    /// with the step that resolved it — ahead of that step's own events, which is where a day of 40
    /// belongs among a step's worth of 90s.
    /// </remarks>
    void StampAt(Stamp when);

    /// <summary>
    /// Ends the step, putting the events it wrote into stamp order.
    /// </summary>
    /// <remarks>
    /// The second of the two rules that keep the log readable in order once events carry days. The
    /// first is a discipline — a system stamps only inside the step it is running in — and this is
    /// its enforcement: system order and the calendar are allowed to disagree within a step, and
    /// the step is what reconciles them.
    /// </remarks>
    void CloseStep();

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

    /// <summary>Which system wrote each event, parallel to <see cref="_events"/>.</summary>
    /// <remarks>
    /// Beside the log rather than on <see cref="HistoryEvent"/>, because it is bookkeeping for one
    /// sort and not a fact about the event. Nothing outside this class asks which system wrote
    /// something, and putting it on the record would export it and invite something to.
    /// </remarks>
    private readonly List<int> _writers = new();

    /// <summary>Where in the log the open step began.</summary>
    private int _stepStart;

    private int _writer;

    /// <summary>The step currently running, or the opening of year zero before one has been.</summary>
    /// <remarks>
    /// Not part of the world's state and never exported: it is scaffolding for the writer, and a
    /// run that resumed mid-year would set it from the step it resumed into rather than restore it.
    /// </remarks>
    public Stamp Now { get; private set; }

    public void OpenStep(Stamp now)
    {
        Now = now;
        _stepStart = _events.Count;
        _writer = 0;
    }

    public void EnterSystem(int index) => _writer = index;

    public void StampAt(Stamp when) => Now = when;

    /// <summary>
    /// Sorts the step's own events into <c>(day, system index, sequence)</c> order.
    /// </summary>
    /// <remarks>
    /// <para><b>Sorted in place rather than buffered and flushed</b>, which is the one place this
    /// departs from the shape the design sketched, and it departs for a reason the design did not
    /// have in front of it: <see cref="World.Tomes"/> reads the log <em>during</em> a step to write
    /// a settlement's annals. Holding a step's events back until it ended would hide the year's own
    /// entries from the tome being written in it, which is a change of history rather than of
    /// plumbing. Appending as before and reordering afterwards leaves every mid-step read seeing
    /// exactly what it sees today.</para>
    ///
    /// <para><b>Reassigning ids is safe, and is the reason the design checked.</b> An event's id
    /// encodes its position in the log and nothing else — nothing in the engine stores one on an
    /// entity, a tome's passages carry entity ids — so a reordering within a step keeps the
    /// property the export depends on rather than breaking a reference.</para>
    ///
    /// <para>Sequence is the last term of the key, so the order is total and an unstable sort
    /// cannot express an opinion. Two events of the same day written by the same system come out in
    /// the order they were written, which is what makes this a no-op for a step in which nothing
    /// claimed a day — every step in the engine today.</para>
    /// </remarks>
    public void CloseStep()
    {
        int count = _events.Count - _stepStart;
        if (count < 2) return;

        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = _stepStart + i;

        Array.Sort(order, (left, right) =>
        {
            int byDay = _events[left].Day.CompareTo(_events[right].Day);
            if (byDay != 0) return byDay;

            int byWriter = _writers[left].CompareTo(_writers[right]);
            return byWriter != 0 ? byWriter : left.CompareTo(right);
        });

        var sorted = new HistoryEvent[count];
        var writers = new int[count];

        for (int i = 0; i < count; i++)
        {
            sorted[i] = _events[order[i]] with { Id = _stepStart + i };
            writers[i] = _writers[order[i]];
        }

        for (int i = 0; i < count; i++)
        {
            _events[_stepStart + i] = sorted[i];
            _writers[_stepStart + i] = writers[i];
        }
    }

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
        _writers.Add(_writer);
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
