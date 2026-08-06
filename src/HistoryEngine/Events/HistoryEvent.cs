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

        if (Id != other.Id || Year != other.Year || Kind != other.Kind) return false;
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
        hash.Add(Kind);
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
            Kind.ToString(),
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

    public override string ToString() => $"[{Year}] {Kind} {Subject}";
}
