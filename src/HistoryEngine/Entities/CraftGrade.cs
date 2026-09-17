using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// A standing inside a trade. Explicit values — part of the export format.
/// </summary>
/// <remarks>
/// <para><b>Beside the craft, not inside it.</b> <see cref="Craft"/> says which trade somebody
/// practises and is taken once for life; this says what they are inside it, and it moves. Before
/// this, everyone was equally a smith at sixteen and at sixty — the same emptiness
/// <see cref="MilitaryRank"/> was made to fill for soldiery, and a craft was the odd career out
/// for not having it.</para>
///
/// <para><b>Three rungs, because that is what the rolls recorded.</b> The guild ladder is the one
/// part of premodern working life that was formally written down, and it had exactly these three
/// stages: bound for a term of years, working for wages, admitted to keep a shop. A fourth rung
/// would be an invention; a shorter ladder would lose the distinction the record was actually
/// kept for, which is who was entitled to be named on the work.</para>
///
/// <para><b>Not a measure of ability.</b> Whether a craftsman was any good is a different axis —
/// see #252, which is deliberately not this — and nothing here scores anybody. A master is
/// somebody a company admitted, which is an institutional fact and the only one the sources have.
/// </para>
/// </remarks>
public enum CraftGrade
{
    /// <summary>Not of a trade. Everyone without a craft, and every child.</summary>
    None = 0,

    /// <summary>Bound for a term. What taking a craft makes somebody.</summary>
    Apprentice = 1,

    /// <summary>Free of the trade and working for wages, with no shop of their own.</summary>
    Journeyman = 2,

    /// <summary>Admitted by a company: keeps a shop, binds apprentices, is named on the work.</summary>
    Master = 3,
}

/// <summary>One stage of a working life, and the year it was reached.</summary>
/// <remarks>
/// <para>The shape <see cref="RankStep"/> has, minus the realm and minus the ending. A grade is
/// not laid down — a man admitted master and later crowned is a master still — so the operative
/// stage is the last entry, which is what keeps <see cref="Figure.Grade"/> a derivation rather
/// than a second copy able to disagree with the history.</para>
///
/// <para>The town rather than the realm, because a company is a town body: a mastery is admitted
/// by the men of one place and means nothing in the next one. It is the town the stage was reached
/// in, not where the man later lived.</para>
///
/// <para>No title. What a culture calls each rung is the pattern <see cref="Culture.TitleFor"/>
/// already has for offices and can follow later; the grade and the craft are enough to name one
/// — see <see cref="World.Grades.Phrase"/>.</para>
/// </remarks>
/// <param name="SettlementId">The town whose trade admitted them. None where the record has no place.</param>
public sealed record CraftStep(CraftGrade Grade, EntityId SettlementId, int Year)
{
    /// <summary>Why they were advanced, in prose: "by the admission of the Weavers of Aldenmoor".</summary>
    public string? Claim { get; init; }
}
