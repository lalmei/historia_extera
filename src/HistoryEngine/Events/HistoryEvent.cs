using HistoryEngine.Core;

namespace HistoryEngine.Events;

/// <summary>
/// One dated occurrence. The event log is the history — everything else is state that
/// produced it.
/// </summary>
/// <remarks>
/// <para><b>Why one flat record instead of a class per event kind.</b> A polymorphic
/// hierarchy is the more natural object model and the wrong shape for this job. It needs
/// discriminated JSON and a viewer that knows every subtype; the viewer would gain a new
/// case for every event the engine learned to emit. Flat means the export is a uniform
/// array, indexing is trivial, and there is no deserialisation ceremony at either end.</para>
///
/// <para><b>Three named slots, not a participant list.</b> <see cref="Subject"/>,
/// <see cref="Object"/> and <see cref="Location"/> cover the overwhelming majority of
/// events — someone did something, to something, somewhere — and giving them fixed names
/// is what lets <see cref="Narration"/> hold one prose template per kind. A bare list of
/// participants would push per-kind knowledge back into whatever renders it.
/// <see cref="Extra"/> takes the overflow: a war's belligerents, a battle's participants.</para>
///
/// <para><see cref="Data"/> carries the small facts prose needs — a population figure, a
/// new tier, a cause of death — as strings, because this is a display payload rather than
/// simulation state. Anything the simulation itself reads back belongs on an entity.</para>
/// </remarks>
public sealed record HistoryEvent(
    int Id,
    int Year,
    EventKind Kind,
    EntityId Subject,
    EntityId Object,
    EntityId Location,
    IReadOnlyList<EntityId>? Extra = null,
    DetMap<string, string>? Data = null)
{
    /// <summary>
    /// The day within <see cref="Year"/> this happened on.
    /// </summary>
    /// <remarks>
    /// <para><b>The year is kept, and that is the migration decision.</b> Every existing read
    /// survives untouched: the per-year index, the viewer's timeline slider, the year filters and
    /// the territory replay all continue to work, and the day is additive detail they adopt when
    /// they have a reason to. It is the same choice schema 9 made when <c>ExportTitle</c> gained
    /// four fields and kept <c>civilizationId</c>.</para>
    ///
    /// <para>Init-only with a default of zero rather than a positional member, so that an event
    /// recorded by a system with nothing finer to say than a year is spelled the way it is
    /// meant — the opening of the year — instead of forcing several hundred call sites to name a
    /// day none of them has yet. Every system in the engine is still
    /// <see cref="Systems.Cadence.Annual"/>, so every day in a world today is zero.</para>
    /// </remarks>
    public int Day { get; init; }

    /// <summary>When this happened, as one comparable value.</summary>
    public Stamp At => new(Year, Day);

    /// <summary>
    /// Whether this belongs to the narrative spine or to the vital register.
    /// </summary>
    /// <remarks>
    /// Init-only with a default of <see cref="Significance.Notable"/>, for the same reason
    /// <see cref="Day"/> is: it is additive detail on a record several hundred call sites already
    /// build, and all but four of them are writing spine. See <see cref="Significance"/> for what
    /// the distinction is for and why the emitting system is what decides it.
    /// </remarks>
    public Significance Significance { get; init; }

    /// <summary>Every entity this event mentions, in slot order. Drives the export's per-entity index.</summary>
    public IEnumerable<EntityId> References()
    {
        if (!Subject.IsNone) yield return Subject;
        if (!Object.IsNone) yield return Object;
        if (!Location.IsNone) yield return Location;

        if (Extra is not null)
        {
            for (int i = 0; i < Extra.Count; i++)
            {
                if (!Extra[i].IsNone) yield return Extra[i];
            }
        }
    }

    public string? DataValue(string key) =>
        Data is not null && Data.TryGetValue(key, out string? value) ? value : null;

    /// <summary>
    /// Value equality including <see cref="Extra"/>.
    /// </summary>
    /// <remarks>
    /// The compiler-generated record equality would compare <see cref="Extra"/> by reference,
    /// because <see cref="IReadOnlyList{T}"/> is a reference type. Two events with identical
    /// content would then compare unequal — a trap for deduplication and for any test asserting
    /// two runs produced the same history, and one that presents as a determinism failure rather
    /// than an equality bug. <see cref="DetMap{TKey,TValue}"/> handles the <see cref="Data"/> half.
    /// </remarks>
    public bool Equals(HistoryEvent? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (Id != other.Id || Year != other.Year || Day != other.Day) return false;
        if (Kind != other.Kind || Significance != other.Significance) return false;
        if (Subject != other.Subject || Object != other.Object || Location != other.Location) return false;
        if (!Equals(Data, other.Data)) return false;

        if (Extra is null || other.Extra is null) return Extra is null && other.Extra is null;
        if (Extra.Count != other.Extra.Count) return false;

        for (int i = 0; i < Extra.Count; i++)
        {
            if (Extra[i] != other.Extra[i]) return false;
        }

        return true;
    }

    public override int GetHashCode() // det:ok — never feeds simulation output
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Year);
        hash.Add(Day);
        hash.Add(Kind);
        hash.Add(Significance);
        hash.Add(Subject);
        hash.Add(Object);
        hash.Add(Location);
        hash.Add(Data);

        if (Extra is not null)
        {
            for (int i = 0; i < Extra.Count; i++) hash.Add(Extra[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// A canonical one-line form, for diffing two histories against each other.
    /// </summary>
    /// <remarks>
    /// Deliberately independent of names and narration, so it stays stable when the naming
    /// milestone replaces every label in the world.
    /// </remarks>
    public string Signature()
    {
        var parts = new List<string>(8)
        {
            Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Day.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Kind.ToString(),
            Significance.ToString(),
            Subject.ToString(),
            Object.ToString(),
            Location.ToString(),
        };

        if (Extra is not null)
        {
            foreach (EntityId id in Extra) parts.Add(id.ToString());
        }

        if (Data is not null)
        {
            foreach (KeyValuePair<string, string> pair in Data)
            {
                parts.Add(pair.Key + "=" + pair.Value);
            }
        }

        return string.Join("|", parts);
    }

    public override string ToString() => $"[{At}] {Kind} {Subject}";
}
