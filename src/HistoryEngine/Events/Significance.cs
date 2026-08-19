namespace HistoryEngine.Events;

/// <summary>
/// Whether an event belongs to the narrative spine of a history or to its parish register.
/// </summary>
/// <remarks>
/// <para><b>The problem this solves is a ratio, not a shortage.</b> A three-century run writes
/// roughly four thousand events, and better than three quarters of them are births, deaths,
/// marriages and appointments among people who never governed anything. The wars, sackings,
/// schisms and famines are all there, at about one line in four — which is to say a reader
/// scrolling the chronicle sees a wall of "died at the age of 70, of old age" with a siege
/// buried somewhere in it. The interesting history is not missing. It is outnumbered.</para>
///
/// <para><b>Nothing is discarded, because it must not be.</b> The obvious fix is to stop
/// recording routine vital events, and it is the wrong one: a figure's page is built from the
/// events that mention them, so suppressing a birth would leave a person who appears in the
/// world fully grown. This marks events instead, and the mark travels into the export, so a
/// reader can be shown the spine by default and the register on request while the log itself
/// stays complete. It is the same instinct as replaying territory from transfers rather than
/// storing a map per year: keep the fact, and decide separately what to show.</para>
///
/// <para><b>The emitter classifies, because only the emitter knows.</b> Whether a death is
/// routine is not a property of <see cref="EventKind.FigureDied"/> — it depends on whether the
/// dead held office, whether the cause was a fever or an assassin, whether a house ended with
/// them. A rule table applied at export time would have to reconstruct all of that from the
/// event's slots, badly. The system holding the figure already has the answer.</para>
///
/// <para><see cref="Notable"/> is zero so that it is what an event is unless someone says
/// otherwise. Several hundred recording sites across the engine describe things that happened
    /// to realms, settlements and faiths, and every one of them is spine by default; the sites
    /// that write vital records are the ones that opt out.</para>
/// </remarks>
public enum Significance
{
    /// <summary>Part of the history proper. The default, and what every non-vital event is.</summary>
    Notable = 0,

    /// <summary>
    /// A true and indexed fact that does not carry the narrative: a cadet's birth, an ordinary
    /// death in bed, a marriage between two people who hold nothing.
    /// </summary>
    /// <remarks>
    /// Routine is not a judgement about the person. It is a statement that the event is already
    /// implied by the entity record — <c>figures[]</c> carries birth year, death year and cause —
    /// so the chronicle line is a second copy of a fact the reader can already see on the page of
    /// whoever it concerns.
    /// </remarks>
    Routine = 1,
}
