using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>How a realm stands to a reading it holds.</summary>
/// <remarks>
/// Arrival is not adoption. A realm that received a book and put it in a cupboard is a different
/// fact from one that teaches out of it, and neither is a statement about whether the reading is
/// true — see <see cref="ClaimVerdict"/>, which nothing here reads.
/// </remarks>
public enum ClaimStanding
{
    /// <summary>Present, and nothing more is claimed about it.</summary>
    Received = 0,

    /// <summary>Held as the realm's account of the thing, and taught as one.</summary>
    Taught = 1,

    /// <summary>Held, and argued with: the realm teaches another reading of the same subject.</summary>
    Disputed = 2,

    /// <summary>Held and not entertained, because the realm already explains it otherwise.</summary>
    SetAside = 3,
}

/// <summary>
/// One dated change in how a realm stood to a reading it holds.
/// </summary>
/// <remarks>
/// A standing begins at <see cref="ClaimStanding.Received"/> with the acquisition that put the
/// reading in the realm and ends with the loss that took it away, so a reader folding
/// <see cref="ClaimTransition"/> and these together has the realm's disposition at any year
/// without a per-year picture of who believed what.
/// </remarks>
public sealed record ClaimStandingChange(
    ClaimRef Claim,
    EntityId RealmId,
    int Year,
    ClaimStanding From,
    ClaimStanding To,
    string Cause);

/// <summary>
/// Whether a realm holding a reading argues with it, teaches it, or leaves it in the cupboard.
/// </summary>
/// <remarks>
/// <para><b>Presence and standing are two facts, not one.</b> <see cref="ClaimTransmission"/>
/// answers whether a realm has a reading at all, which is settled by carriers and nothing else.
/// This answers what the realm makes of it, which is settled by the realm — and the two are
/// allowed to disagree for centuries, because that is what it means for a foreign account to
/// arrive somewhere that already has one.</para>
///
/// <para><b>Never from the verdict.</b> What the sky said and what a realm thinks are independent.
/// A refuted reading with patrons is the interesting case, not a bug to be tidied, so no path here
/// may read <see cref="ClaimVerdict"/>; a test asserts the word does not occur in this file.</para>
///
/// <para><b>The disposition inputs are the ones that already decide these things.</b>
/// <see cref="SkyClaims"/> weighs a person's learning against their piety and their faith's
/// zealotry to pick a register; a realm's disposition toward an incoming reading is the same
/// weighing at the scale of the realm — its effective values, its state faith's temper — rather
/// than a parallel set of dials nobody else uses.</para>
///
/// <para><b>Nothing rolls.</b> As with transmission, and for the same reason: a standing that
/// wobbled on a die would say nothing about the realm holding it.</para>
/// </remarks>
public static class ClaimStandings
{
    /// <summary>How inclined a realm has to be before it teaches a reading, and shelves one.</summary>
    /// <remarks>
    /// Two bands, entered and left at different heights, because a realm's effective values drift
    /// a little every year and a standing that flipped whenever they crossed a line would be
    /// noise wearing the costume of a change of mind.
    /// </remarks>
    private const double TeachesAbove = 0.50;

    private const double StopsTeachingBelow = 0.40;

    private const double SetsAsideBelow = 0.15;

    private const double TakesBackUpAbove = 0.22;

    /// <summary>
    /// Reconsiders every realm's standing toward what it holds, and records what changed.
    /// </summary>
    /// <remarks>
    /// Run after transmission, so a reading acquired this year is weighed the year it arrives and
    /// one lost this year is not weighed at all.
    /// </remarks>
    public static void Weigh(WorldState world, int year)
    {
        int count = world.ClaimHoldings.Count;
        if (count == 0) return;

        // Weighed in three passes rather than one, so nothing depends on the order holdings
        // happen to sit in: every realm's inclination first, then what each realm makes of each
        // reading on its own, then the readings that are held against one another.
        var scores = new double[count];
        var standing = new ClaimStanding[count];

        for (int i = 0; i < count; i++)
        {
            scores[i] = Receptivity(world, world.ClaimHoldings[i]);
        }

        for (int i = 0; i < count; i++)
        {
            standing[i] = Weighed(world.ClaimHoldings[i].Standing, scores[i]);
        }

        // Weighed against what each realm taught before any of this year's disputes were
        // settled, so no reading is spared by another having been demoted first.
        var taught = (ClaimStanding[])standing.Clone();
        for (int i = 0; i < count; i++)
        {
            if (taught[i] != ClaimStanding.Taught) continue;
            if (Outargued(world, scores, taught, i)) standing[i] = ClaimStanding.Disputed;
        }

        for (int i = 0; i < count; i++)
        {
            ClaimHolding holding = world.ClaimHoldings[i];
            if (standing[i] == holding.Standing) continue;

            world.ClaimHoldings[i] = holding with { Standing = standing[i], StandingSince = year };
            world.ClaimStandingChanges.Add(new ClaimStandingChange(
                holding.Claim,
                holding.RealmId,
                year,
                holding.Standing,
                standing[i],
                Cause(holding.Standing, standing[i])));
        }
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// What the realm currently makes of this reading, given how far it is inclined toward it.
    /// </summary>
    /// <remarks>
    /// Where the score sits in neither band the standing stands: the middle of the range is a
    /// realm that has the thing and has not made its mind up, which is where most holdings live.
    /// </remarks>
    private static ClaimStanding Weighed(ClaimStanding was, double score)
    {
        if (score >= TeachesAbove) return ClaimStanding.Taught;
        if (score <= SetsAsideBelow) return ClaimStanding.SetAside;

        // A reading held against another is still one the realm carries as an account of the
        // thing, so it leaves the teaching band on the same terms a taught one does.
        bool held = was is ClaimStanding.Taught or ClaimStanding.Disputed;
        if (held && score >= StopsTeachingBelow) return ClaimStanding.Taught;
        if (was == ClaimStanding.SetAside && score <= TakesBackUpAbove) return ClaimStanding.SetAside;

        return ClaimStanding.Received;
    }

    /// <summary>
    /// How far a realm is inclined toward a reading that reached it.
    /// </summary>
    /// <remarks>
    /// <para>Learning against piety, tempered by the faith's zealotry — the weighing
    /// <see cref="SkyClaims"/> does for a person, read off the realm instead.</para>
    ///
    /// <para>Then the friction that makes this record worth having: a reading made under another
    /// faith is a foreign account of the lights, and a faith with a strong one of its own is
    /// exactly the thing that sets it aside. A realm with no state faith has no such account and
    /// no such friction.</para>
    /// </remarks>
    private static double Receptivity(WorldState world, ClaimHolding holding)
    {
        if (!world.Civilizations.Contains(holding.RealmId)) return 0.0;

        Civilization realm = world.Civilizations[holding.RealmId];
        Religion? faith = world.Religions.Contains(realm.StateReligionId)
            ? world.Religions[realm.StateReligionId]
            : null;

        double inclination = 0.30
            + (realm.EffectiveValues.Learning * 0.55)
            - (realm.EffectiveValues.Piety * 0.22)
            - ((faith?.Character.Zealotry ?? 0.0) * 0.18);

        if (faith is null || !Foreign(world, faith, holding.Claim)) return inclination;

        return inclination
            - (faith.Character.Zealotry * 0.30)
            - ((1.0 - faith.Character.Tolerance) * 0.12);
    }

    /// <summary>Whether the reading came out of a faith this one does not recognise as its own.</summary>
    private static bool Foreign(WorldState world, Religion faith, ClaimRef subject)
    {
        if (!world.Figures.Contains(subject.ClaimantId)) return false;

        Figure claimant = world.Figures[subject.ClaimantId];
        if (claimant.ReligionId == faith.Id) return false;
        if (!world.Religions.Contains(claimant.ReligionId)) return true;

        return !faith.KindredTo(world.Religions[claimant.ReligionId]);
    }

    /// <summary>
    /// Whether the realm holds a better-placed reading of the same subject that this contradicts.
    /// </summary>
    /// <remarks>
    /// Two accounts of one comet cannot both be the account a realm teaches. A reading is
    /// disputed where the realm teaches another one that contradicts it: the realm has it, and
    /// argues with it. The one that keeps the floor is the one the realm is most inclined toward,
    /// ties going to the older claim so the answer does not depend on iteration luck. Readings
    /// that agree — the same register and the same number — are one account held twice and
    /// dispute nothing, and a reading nobody teaches is argued with by nobody.
    /// </remarks>
    private static bool Outargued(
        WorldState world, double[] scores, ClaimStanding[] taught, int index)
    {
        ClaimHolding mine = world.ClaimHoldings[index];
        if (Reading(world, mine.Claim) is not Claim ours) return false;

        for (int i = 0; i < world.ClaimHoldings.Count; i++)
        {
            if (i == index) continue;

            ClaimHolding other = world.ClaimHoldings[i];
            if (other.RealmId != mine.RealmId) continue;
            if (taught[i] != ClaimStanding.Taught) continue;
            if (Reading(world, other.Claim) is not Claim theirs) continue;
            if (theirs.Subject != ours.Subject) continue;
            if (theirs.Register == ours.Register && theirs.IntervalYears == ours.IntervalYears)
            {
                continue;
            }

            if (scores[i] > scores[index]) return true;
            if (scores[i] < scores[index]) continue;

            // Even inclination: the reading the realm has argued from longer keeps the floor.
            if (theirs.Year < ours.Year) return true;
            if (theirs.Year > ours.Year) continue;
            if (other.Claim.ClaimantId.CompareTo(mine.Claim.ClaimantId) < 0) return true;
        }

        return false;
    }

    /// <summary>The claim a holding is of, or null where the claimant has left the world.</summary>
    private static Claim? Reading(WorldState world, ClaimRef subject)
    {
        if (!world.Figures.Contains(subject.ClaimantId)) return null;

        foreach (Claim claim in world.Figures[subject.ClaimantId].Claims)
        {
            if (claim.Id == subject.ClaimId) return claim;
        }

        return null;
    }

    /// <summary>Why the standing moved, in the terms the change itself is in.</summary>
    /// <remarks>
    /// Named off the pair rather than off whatever number crossed a threshold: what a chronicle
    /// would say happened, which is that a realm took a reading up, argued with it, stopped
    /// teaching it, or shelved it.
    /// </remarks>
    private static string Cause(ClaimStanding from, ClaimStanding to) => to switch
    {
        ClaimStanding.Taught => from == ClaimStanding.Disputed
            ? "the reading it was held against gave way"
            : "taken up by the realm's learned",
        ClaimStanding.Disputed => "held against the reading the realm teaches",
        ClaimStanding.SetAside => "set aside where the faith already explains it",
        _ => from == ClaimStanding.Disputed
            ? "the dispute lapsed with the reading it was against"
            : from == ClaimStanding.SetAside
                ? "no longer set aside"
                : "no longer taught",
    };
}
