using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// A grade in a realm's army. Explicit values — part of the export format.
/// </summary>
/// <remarks>
/// <para><b>Below the seat, not beside it.</b> <see cref="OfficeKind.Marshal"/> is a post a court
/// grants and takes away; this is what a soldier is when nobody has granted them anything. The two
/// meet at the top — a marshal is the realm's ranking soldier by definition, and
/// <see cref="World.Ranks.Commander"/> is the rung his appointment puts him on — but they answer
/// different questions. "Who commands the host" has one answer per realm. "What is this person"
/// has one per soldier, and before this it had none: of everyone the chronicle followed into arms,
/// all but the one who was made marshal spent forty years being exactly what they were at
/// sixteen.</para>
///
/// <para><b>Five rungs, because a career has to be able to finish.</b> A soldier who takes to arms
/// at sixteen and dies at sixty has perhaps four promotions in them at the rates the model uses, so
/// a longer ladder would leave every life ending in the middle of it and a shorter one would put
/// half the army at the top. The names here are functional; what a realm actually calls each rung
/// is <see cref="Culture.RankTitle"/>, and a reader learns what kind of army it is from the
/// vocabulary without being told a rule.</para>
/// </remarks>
public enum MilitaryRank
{
    /// <summary>Not of the army. Everyone who never took to arms, and every child.</summary>
    None = 0,

    /// <summary>Sworn in and nothing more. What taking to arms makes somebody.</summary>
    Recruit = 1,

    /// <summary>Of the line: has served, and is who the levy means.</summary>
    Soldier = 2,

    /// <summary>Leads a handful. The first rung that is a post rather than an attainment.</summary>
    FileLeader = 3,

    /// <summary>Leads a wing of the host, and is who a court notices when it wants a marshal.</summary>
    Captain = 4,

    /// <summary>Leads the host in the field. One to a realm, and the marshal's rung when it has one.</summary>
    Commander = 5,
}

/// <summary>One rung of a military career, and the year it was reached.</summary>
/// <remarks>
/// The same shape <see cref="OfficeHolding"/> has, minus the ending: a rank is not laid down. A
/// soldier raised to captain and then made governor was a captain still, and the chronicle that
/// closed the rank when the career changed would be unable to say why the court reached for him.
/// The current rung is the last entry, which is what makes <see cref="Figure.Rank"/> a derivation
/// rather than a second copy that can disagree with the history.
/// </remarks>
/// <param name="CivilizationId">The realm served. A rank is that realm's to give.</param>
/// <param name="Title">What that realm called the rung, at the year it was reached.</param>
public sealed record RankStep(
    MilitaryRank Rank,
    string Title,
    EntityId CivilizationId,
    int Year)
{
    /// <summary>Why they were raised, in prose: "for service at the Battle of Ashfen".</summary>
    public string? Claim { get; init; }
}
