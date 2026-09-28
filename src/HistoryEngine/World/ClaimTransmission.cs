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
    EntityId SettlementId,
    string? Cause = null);

/// <summary>What a realm currently holds, on what, and what it makes of it.</summary>
/// <remarks>
/// <see cref="Standing"/> is not derived here and is deliberately not part of what this file
/// recomputes: having a reading and holding with it are two facts, and <see cref="ClaimStandings"/>
/// owns the second. A holding begins <see cref="ClaimStanding.Received"/> — arrival is not
/// adoption — and keeps whatever standing it has reached while the realm goes on holding it.
/// </remarks>
public sealed record ClaimHolding(
    ClaimRef Claim,
    EntityId RealmId,
    int Since,
    ClaimCarrierKind Carrier,
    EntityId CarrierId,
    EntityId SettlementId)
{
    /// <summary>Whether the selected text exemplar is a settlement copy rather than the original.</summary>
    public bool IsCopy { get; init; }

    /// <summary>What the realm makes of it. Never derived from a verdict.</summary>
    public ClaimStanding Standing { get; init; } = ClaimStanding.Received;

    /// <summary>The year it came to stand that way. The year of arrival until it changes.</summary>
    public int StandingSince { get; init; }
}

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
/// abandoned and sacked by the systems that own those decisions, books and the copies of them
/// go with the towns that kept them, and people die; when the last of those is gone from a
/// realm, the realm no longer holds the claim and the chronicle says which carrier was the last
/// one.</para>
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
                // Made by now and not yet burned. A copy destroyed in a sack stops carrying the
                // reading the year it went, and the record of it having existed stays put.
                if (!copy.SurvivedTo(year)) continue;
                Note(world, holders, subject, work.Id, copy.SettlementId, year, isCopy: true);
            }
        }

        // Gone: a realm that held it and has nothing left to hold it up.
        for (int i = world.ClaimHoldings.Count - 1; i >= 0; i--)
        {
            ClaimHolding held = world.ClaimHoldings[i];
            if (held.Claim != subject || holders.ContainsKey(held.RealmId)) continue;

            // Where the carrier actually went from. For a text that is wherever the holding says
            // the book sat; for an author it is not, because the holding's town is only refreshed
            // while they are alive and this pass runs once a year. Somebody posted to a new town
            // in the autumn and dead before the next spring had their reading recorded as lost
            // from the town they had already left — the last address the refresh managed to see,
            // rather than the one they died at.
            EntityId wentFrom = WentFrom(world, held, claimant, year);

            string cause = LossCause(world, held, claimant, year);
            world.ClaimHoldings.RemoveAt(i);
            world.ClaimTransitions.Add(new ClaimTransition(
                subject,
                held.RealmId,
                year,
                ClaimTransitionKind.Lost,
                held.Carrier,
                held.CarrierId,
                wentFrom,
                cause));

            DetMap<string, string> lost = Chronicle.Data(
                ("reading", claim.Reading),
                ("carrier", held.Carrier == ClaimCarrierKind.Claimant ? "its author" : "the last copy"));

            lost["cause"] = cause;

            world.Chronicle.Record(
                year,
                EventKind.ClaimLost,
                claimant.Id,
                obj: held.RealmId,
                location: wentFrom,
                data: lost,
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
                pair.Value.SettlementId,
                AcquisitionCause(world, pair.Value, claim, year)));

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

    /// <summary>
    /// The town a lost carrier went from.
    /// </summary>
    /// <remarks>
    /// <para>A holding records where its carrier sat when the pass last looked, and for a book
    /// that is the answer: the book is where the holding says it is until something moves or
    /// burns it, and both of those go through this file.</para>
    ///
    /// <para>An author is different, because the thing carrying the reading walks. The holding is
    /// re-seated only on a year when the claimant is still alive, so the last refresh before a
    /// death can be a year stale — and a man posted to a governorship in one year and dead in the
    /// next has his reading recorded as lost from the town he was posted away from. Read off his
    /// own residence history instead, at the year the pass compared against, which is the state
    /// that made the loss visible.</para>
    ///
    /// <para>Falls back to the holding wherever the history has nothing to say, so a claimant with
    /// no recorded address before the loss is no worse off than before.</para>
    /// </remarks>
    private static EntityId WentFrom(
        WorldState world, ClaimHolding held, Figure claimant, int year)
    {
        if (held.Carrier != ClaimCarrierKind.Claimant) return held.SettlementId;

        EntityId where = EntityId.None;
        foreach (Residence residence in claimant.Residences)
        {
            if (residence.FromYear >= year) break;
            where = residence.SettlementId;
        }

        return world.Settlements.Contains(where) ? where : held.SettlementId;
    }

    // Describe the selected exemplar while its loss is visible. Never consult its eventual
    // fate: a surviving copy can leave a realm decades before that copy burns.
    private static string LossCause(WorldState world, ClaimHolding held, Figure claimant, int year)
    {
        if (held.Carrier == ClaimCarrierKind.Claimant)
        {
            return !claimant.IsAlive && claimant.DeathYear <= year
                ? "its author died"
                : "its author left the realm";
        }

        if (world.Settlements.Contains(held.SettlementId))
        {
            Settlement place = world.Settlements[held.SettlementId];
            if (place.AbandonedYear is int abandoned && abandoned <= year)
                return "the town holding it was abandoned";
        }

        if (world.Artifacts.Contains(held.CarrierId))
        {
            Artifact work = world.Artifacts[held.CarrierId];
            if (held.IsCopy && work.TomeContents is TomeContents contents)
            {
                foreach (TomeCopy copy in contents.Copies)
                {
                    if (copy.SettlementId == held.SettlementId && copy.LostYear is int lost && lost <= year)
                        return "its last local copy was lost" + (copy.LostHow is { Length: > 0 } how ? " (" + how + ")" : "");
                }
            }
            else if (work.LostYear is int lost && lost <= year)
            {
                return "the original work was lost";
            }
        }

        if (world.Settlements.Contains(held.SettlementId)
            && world.Settlements[held.SettlementId].CivilizationId != held.RealmId)
            return "the town holding it left the realm";

        if (!held.IsCopy && world.Artifacts.Contains(held.CarrierId)
            && world.Artifacts[held.CarrierId].HolderId != held.SettlementId)
            return "the original work left the realm";

        return "no surviving carrier remained in the realm";
    }

    private static string AcquisitionCause(WorldState world, ClaimHolding holding, Claim claim, int year)
    {
        if (holding.Carrier == ClaimCarrierKind.Claimant)
            return claim.Year == year && claim.RealmId == holding.RealmId
                ? "its author made the reading here"
                : "its author carried the reading into the realm";
        if (holding.IsCopy) return "a surviving copy became available in the realm";
        return world.Artifacts[holding.CarrierId].CreatedYear == year
            ? "a work carrying the reading was written here"
            : "the original work became available in the realm";
    }

    /// <summary>Records a text-carried holding, if that settlement is somewhere a realm still has.</summary>
    private static void Note(
        WorldState world,
        DetMap<EntityId, ClaimHolding> holders,
        ClaimRef subject,
        EntityId workId,
        EntityId settlementId,
        int year,
        bool isCopy = false)
    {
        if (settlementId.IsNone || !world.Settlements.Contains(settlementId)) return;

        Settlement place = world.Settlements[settlementId];
        if (!place.IsActive || place.CivilizationId.IsNone) return;

        // A living claimant is the better answer where there is one, and the first text wins
        // otherwise: works are walked in id order, so this does not depend on iteration luck.
        if (holders.ContainsKey(place.CivilizationId)) return;

        holders[place.CivilizationId] = new ClaimHolding(
            subject, place.CivilizationId, year, ClaimCarrierKind.Text, workId, settlementId)
        { IsCopy = isCopy };
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
            && held.SettlementId == carried.SettlementId
            && held.IsCopy == carried.IsCopy)
        {
            return;
        }

        world.ClaimHoldings[index] = held with
        {
            Carrier = carried.Carrier,
            CarrierId = carried.CarrierId,
            SettlementId = carried.SettlementId,
            IsCopy = carried.IsCopy,
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
