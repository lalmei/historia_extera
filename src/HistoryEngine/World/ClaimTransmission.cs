using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>What became of a claim in one realm.</summary>
public enum ClaimTransitionKind
{
    Acquired = 0,
    Lost = 1,
}

/// <summary>What was holding a claim up when it arrived, or when it stopped.</summary>
public enum ClaimCarrierKind
{
    /// <summary>The person who made it, for as long as they lived.</summary>
    Claimant = 0,

    /// <summary>A written work in a settlement of the realm.</summary>
    Text = 1,
}

/// <summary>
/// One dated change in what a realm held, and what carried it.
/// </summary>
/// <remarks>
/// The export keeps these rather than a per-year picture of who knew what, for the reason
/// territory is replayed from transfers: the state at any year folds out of the transitions, and
/// the transitions say something a snapshot never can, which is why it changed.
/// </remarks>
public sealed record ClaimTransition(
    ClaimRef Claim,
    EntityId RealmId,
    int Year,
    ClaimTransitionKind Kind,
    ClaimCarrierKind Carrier,
    EntityId CarrierId,
    EntityId SettlementId);

/// <summary>What a realm currently holds, and on what.</summary>
public sealed record ClaimHolding(
    ClaimRef Claim,
    EntityId RealmId,
    int Since,
    ClaimCarrierKind Carrier,
    EntityId CarrierId,
    EntityId SettlementId);

/// <summary>
/// How a claim reaches a realm its author never saw, and how it stops being held there.
/// </summary>
/// <remarks>
/// <para><b>Nothing here rolls.</b> Not one draw, not one chance, not one rate. A claim moves
/// because something the chronicle already recorded moved it — a copy the circulation model
/// decided to make, into a town that model chose — and it stops being held because the carriers
/// holding it up stopped surviving. That is the whole design rule, and the absence of an
/// <c>IRng</c> in this file is what enforces it. A diffusion coefficient over trading pairs would
/// be shorter to write and would put a claim in a realm nothing carried it to.</para>
///
/// <para><b>Holding is derived, and the transitions are the record.</b> Each year this recomputes
/// who holds what from carriers that exist, and writes down only the differences. So a claim
/// cannot be held by a realm with nothing to hold it, and no state can drift away from the world
/// it is supposed to describe.</para>
///
/// <para><b>Two carriers, in order of how a chronicle would name them.</b> A living claimant
/// carries their own reading; a written work carries it after them, wherever a surviving exemplar
/// sits. A claimant preferred over a text is not an accident: while the person is alive, they are
/// the reason the realm has it.</para>
///
/// <para><b>Loss is the same rule read backwards.</b> Nothing destroys knowledge here. Towns are
/// abandoned and sacked by the systems that own those decisions, books go with the towns that
/// kept them, and people die; when the last of those is gone from a realm, the realm no longer
/// holds the claim and the chronicle says which carrier was the last one.</para>
/// </remarks>
public static class ClaimTransmission
{
    /// <summary>
    /// Brings every realm's holdings up to date for this year, and records what changed.
    /// </summary>
    public static void Trace(WorldState world, int year)
    {
        List<Artifact> carriers = CarryingWorks(world);

        foreach (Figure claimant in world.Figures)
        {
            if (claimant.Claims.Count == 0) continue;

            foreach (Claim claim in claimant.Claims)
            {
                if (claim.Year > year) continue;
                Reconcile(world, claimant, claim, carriers, year);
            }
        }
    }

    /// <summary>
    /// The claims a work composed in this realm this year sets down.
    /// </summary>
    /// <remarks>
    /// What the composing realm holds, and nothing else. A scribe cannot write down a reading
    /// nobody around them has, and a book written in a realm that lost its register carries what
    /// that realm has now rather than what it once had.
    /// </remarks>
    public static void Compose(WorldState world, TomeContents contents, EntityId realmId, int year)
    {
        foreach (ClaimHolding holding in world.ClaimHoldings)
        {
            if (holding.RealmId != realmId) continue;
            if (holding.Since > year) continue;
            if (contents.Carries.Contains(holding.Claim)) continue;

            contents.Carries.Add(holding.Claim);
        }
    }

    // -----------------------------------------------------------------------

    private static void Reconcile(
        WorldState world, Figure claimant, Claim claim, List<Artifact> carriers, int year)
    {
        var subject = new ClaimRef(claimant.Id, claim.Id);
        var holders = new DetMap<EntityId, ClaimHolding>();

        // While its author lives, the realm they belong to has it from them and needs nothing
        // written down. This is also why a claim can be lost the year somebody dies.
        if (claimant.IsAlive && !claimant.CivilizationId.IsNone)
        {
            holders[claimant.CivilizationId] = new ClaimHolding(
                subject,
                claimant.CivilizationId,
                year,
                ClaimCarrierKind.Claimant,
                claimant.Id,
                world.ResidenceOf(claimant));
        }

        foreach (Artifact work in carriers)
        {
            if (work.TomeContents is not TomeContents contents) continue;
            if (!contents.Carries.Contains(subject)) continue;

            // The work itself, where it still sits somewhere that has not been abandoned.
            Note(world, holders, subject, work.Id, work.HolderId, year);

            foreach (TomeCopy copy in contents.Copies)
            {
                if (copy.Year > year) continue;
                Note(world, holders, subject, work.Id, copy.SettlementId, year);
            }
        }

        // Gone: a realm that held it and has nothing left to hold it up.
        for (int i = world.ClaimHoldings.Count - 1; i >= 0; i--)
        {
            ClaimHolding held = world.ClaimHoldings[i];
            if (held.Claim != subject || holders.ContainsKey(held.RealmId)) continue;

            world.ClaimHoldings.RemoveAt(i);
            world.ClaimTransitions.Add(new ClaimTransition(
                subject,
                held.RealmId,
                year,
                ClaimTransitionKind.Lost,
                held.Carrier,
                held.CarrierId,
                held.SettlementId));

            world.Chronicle.Record(
                year,
                EventKind.ClaimLost,
                claimant.Id,
                obj: held.RealmId,
                location: held.SettlementId,
                data: Chronicle.Data(
                    ("reading", claim.Reading),
                    ("carrier", held.Carrier == ClaimCarrierKind.Claimant ? "its author" : "the last copy")),
                significance: Significance.Notable);
        }

        // Arrived: a realm with a carrier and no record of having had one.
        foreach (KeyValuePair<EntityId, ClaimHolding> pair in holders)
        {
            int existing = IndexOfHolding(world, subject, pair.Key);
            if (existing >= 0)
            {
                Reseat(world, existing, pair.Value);
                continue;
            }

            world.ClaimHoldings.Add(pair.Value);
            world.ClaimTransitions.Add(new ClaimTransition(
                subject,
                pair.Key,
                year,
                ClaimTransitionKind.Acquired,
                pair.Value.Carrier,
                pair.Value.CarrierId,
                pair.Value.SettlementId));

            // The realm it was made in is not news; a realm it was carried to is.
            if (pair.Value.Carrier != ClaimCarrierKind.Text) continue;

            world.Chronicle.Record(
                year,
                EventKind.ClaimCarried,
                claimant.Id,
                obj: pair.Key,
                location: pair.Value.SettlementId,
                data: Chronicle.Data(("reading", claim.Reading)),
                significance: Significance.Routine);
        }
    }

    /// <summary>Records a text-carried holding, if that settlement is somewhere a realm still has.</summary>
    private static void Note(
        WorldState world,
        DetMap<EntityId, ClaimHolding> holders,
        ClaimRef subject,
        EntityId workId,
        EntityId settlementId,
        int year)
    {
        if (settlementId.IsNone || !world.Settlements.Contains(settlementId)) return;

        Settlement place = world.Settlements[settlementId];
        if (!place.IsActive || place.CivilizationId.IsNone) return;

        // A living claimant is the better answer where there is one, and the first text wins
        // otherwise: works are walked in id order, so this does not depend on iteration luck.
        if (holders.ContainsKey(place.CivilizationId)) return;

        holders[place.CivilizationId] = new ClaimHolding(
            subject, place.CivilizationId, year, ClaimCarrierKind.Text, workId, settlementId);
    }

    /// <summary>
    /// Moves a holding onto what is carrying it now, keeping the year the realm came by it.
    /// </summary>
    /// <remarks>
    /// A realm can keep a reading while the thing holding it up changes underneath: the author
    /// dies and the books they left behind take over, or the town with the first copy is
    /// abandoned and a second copy elsewhere becomes the reason the realm still has it. That is
    /// not an arrival — the realm never stopped holding it — so nothing is written to the
    /// chronicle here. What it does buy is a loss that names the carrier that actually went,
    /// rather than blaming an author a century in the ground for a book that burned.
    /// </remarks>
    private static void Reseat(WorldState world, int index, ClaimHolding carried)
    {
        ClaimHolding held = world.ClaimHoldings[index];
        if (held.Carrier == carried.Carrier
            && held.CarrierId == carried.CarrierId
            && held.SettlementId == carried.SettlementId)
        {
            return;
        }

        world.ClaimHoldings[index] = held with
        {
            Carrier = carried.Carrier,
            CarrierId = carried.CarrierId,
            SettlementId = carried.SettlementId,
        };
    }

    /// <summary>Where this realm's holding of a claim sits, or -1 if it has none.</summary>
    private static int IndexOfHolding(WorldState world, ClaimRef subject, EntityId realmId)
    {
        for (int i = 0; i < world.ClaimHoldings.Count; i++)
        {
            ClaimHolding holding = world.ClaimHoldings[i];
            if (holding.Claim == subject && holding.RealmId == realmId) return i;
        }

        return -1;
    }

    /// <summary>Every written work in the world that carries a claim at all.</summary>
    private static List<Artifact> CarryingWorks(WorldState world)
    {
        var works = new List<Artifact>();
        foreach (Artifact artifact in world.Artifacts)
        {
            if (artifact.TomeContents is { Carries.Count: > 0 }) works.Add(artifact);
        }

        return works;
    }
}
