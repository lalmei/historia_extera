using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>
/// What a scheduled entry is for.
/// </summary>
/// <remarks>
/// <para>Part of the ordering key, so these values are numbered explicitly: the order two entries
/// due on the same day come off the docket in is a property of this enum's numbering, and a
/// reordering of the declarations would be a change of history rather than a tidy-up.</para>
///
/// <para><b>Declared ahead of its consumers, deliberately.</b> The docket's total order needs a
/// stable domain before anything can be scheduled against it, and the clock is being built one
/// stage before anything is allowed to use it — the whole point of that stage being that it can be
/// reviewed against a fingerprint proving it changed nothing. Nothing schedules any of these
/// yet.</para>
/// </remarks>
public enum DocketKind
{
    /// <summary>Not scheduled. The default value, and never a real entry.</summary>
    None = 0,

    /// <summary>A siege reaches its decision: carried, lifted, or relieved.</summary>
    SiegeResolves = 1,

    /// <summary>An outbreak takes its next step, while it is still running.</summary>
    OutbreakStep = 2,

    /// <summary>Something in transit reaches where it was going — settlers, a relic, a copied tome.</summary>
    Arrival = 3,
}

/// <summary>One piece of scheduled work, and the key it is ordered by.</summary>
/// <param name="Sequence">
/// Position in the order this entry was scheduled, unique for the life of the docket. It is the
/// last term of the total order and the reason there always is one — see <see cref="Docket"/>.
/// </param>
public readonly record struct DocketEntry(
    Stamp Due, DocketKind Kind, EntityId Subject, int Sequence);

/// <summary>
/// Scheduled work, in stamp order.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The day is never looped over in this engine. It is reached two
/// ways only: as a stamp on something that happened, and as a due date on something scheduled. A
/// siege that will resolve in forty days costs one entry here, not forty ticks — which is what
/// makes cost scale with the number of episodes in play rather than with the length of the run, the
/// same property <c>TerrainDisciplineTests</c> already asserts for terrain sampling.</para>
///
/// <para><b>A sorted list with binary-search insert, in the shape of <see cref="DetMap{TKey,
/// TValue}"/> and for the same reason:</b> what comes off the queue next has to be a property of
/// the keys and not of insertion history. A priority queue would do the same job and is the obvious
/// alternative; <see cref="PriorityQueue{TElement, TPriority}"/> is explicitly documented as an
/// unstable heap, so entries with equal priority come out in an order that depends on how the heap
/// happened to sift — which is precisely the class of bug this engine spends a whole test file
/// guarding against.</para>
///
/// <para><b>The total order is <c>(absolute day, kind, subject index, sequence)</c>, all
/// integers.</b> No floating point anywhere in it, so ordering cannot differ by an ULP between two
/// runtimes. It is total because <see cref="DocketEntry.Sequence"/> is unique, which means two
/// otherwise identical entries come off in the order they went on; every term before it is a
/// property of the entry itself, so scheduling an unrelated episode first cannot move an existing
/// one.</para>
///
/// <para><b>Not exported</b>, on the argument <see cref="WorldState.Outbreaks"/> already carries:
/// it is state a system holds between steps, not something history refers to afterwards. What
/// survives it is the events it wrote. It lives on <see cref="WorldState"/> all the same, which is
/// what makes it survive the <c>Advance</c>-versus-<c>Run</c> split determinism test.</para>
/// </remarks>
public sealed class Docket
{
    private readonly Calendar _calendar;
    private readonly List<DocketEntry> _entries = new();
    private readonly IComparer<DocketEntry> _order;

    private int _sequence;

    public Docket(Calendar calendar)
    {
        _calendar = calendar;
        _order = new StampOrder(calendar);
    }

    public int Count => _entries.Count;

    /// <summary>Everything scheduled, earliest first. Read-only: schedule and take are the way in and out.</summary>
    public IReadOnlyList<DocketEntry> Entries => _entries;

    /// <summary>Puts work on the docket for <paramref name="due"/>.</summary>
    public void Schedule(Stamp due, DocketKind kind, EntityId subject)
    {
        var entry = new DocketEntry(due, kind, subject, _sequence);
        _sequence++;

        int found = _entries.BinarySearch(entry, _order);

        // The sequence number makes every entry distinct, so the search never finds a match and
        // the complement is always the insertion point. The other branch is written out rather
        // than asserted because throwing would be a strange way to learn that two entries compared
        // equal, and inserting at the match is the same answer anyway.
        _entries.Insert(found < 0 ? ~found : found, entry);
    }

    /// <summary>
    /// Takes the earliest entry due at or before <paramref name="now"/>, if there is one.
    /// </summary>
    /// <remarks>
    /// Due <em>at or before</em>, not due exactly: a step that lands past an entry's date still
    /// owes the work. A system drains in a loop until this returns false, and a step is over when
    /// nothing is left owing.
    /// </remarks>
    public bool TryTakeDue(Stamp now, out DocketEntry entry)
    {
        if (_entries.Count > 0 && _calendar.AbsoluteDay(_entries[0].Due) <= _calendar.AbsoluteDay(now))
        {
            entry = _entries[0];
            _entries.RemoveAt(0);
            return true;
        }

        entry = default;
        return false;
    }

    /// <summary>
    /// Orders by absolute day rather than by <see cref="Stamp"/>.
    /// </summary>
    /// <remarks>
    /// The two agree for every stamp whose day lies inside its year, and they disagree for exactly
    /// the stamps a docket is built to hold: <c>now + 40 days</c> can land past the end of the
    /// year, and it must sort into the next one rather than at the end of this one. That is why the
    /// design names absolute day as the first term and why this comparer holds a calendar at all.
    /// </remarks>
    private sealed class StampOrder : IComparer<DocketEntry>
    {
        private readonly Calendar _calendar;

        public StampOrder(Calendar calendar) => _calendar = calendar;

        public int Compare(DocketEntry a, DocketEntry b)
        {
            int byDay = _calendar.AbsoluteDay(a.Due).CompareTo(_calendar.AbsoluteDay(b.Due));
            if (byDay != 0) return byDay;

            int byKind = ((int)a.Kind).CompareTo((int)b.Kind);
            if (byKind != 0) return byKind;

            // The subject's index alone, per the design. Within one kind the subject's entity kind
            // is fixed — every SiegeResolves names a battle — so the index is the discriminator and
            // carrying the entity kind as well would order nothing that the sequence does not.
            int bySubject = a.Subject.Index.CompareTo(b.Subject.Index);
            return bySubject != 0 ? bySubject : a.Sequence.CompareTo(b.Sequence);
        }
    }
}
