using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// What happens to made things: where they are made, who claims them, and how they are lost.
/// </summary>
/// <remarks>
/// Shared for the same reason <see cref="Realms"/> is. An artifact leaves a settlement three
/// entirely different ways — carried off by an army, buried with a town nobody could feed any
/// longer, or burned in a disaster — and each is written by a different system. Ownership is a
/// fourth motion: a crown passes with a throne, a book is given to a monastery, a jewel sits in
/// a treasury until somebody claims it. If each system wrote its own version, one of them would
/// eventually forget to clear the holder or record the move, and the symptom would be a treasure
/// in two places at once.
/// </remarks>
public static class Treasures
{
    /// <summary>
    /// Makes a thing, and records it.
    /// </summary>
    /// <remarks>
    /// The name is composed here rather than generated, exactly as war names are: "the Crown of
    /// Aigionanvos" is a description a chronicler writes, and composing it once at creation is
    /// what keeps every later reference to the object worded identically.
    /// </remarks>
    public static Artifact Create(
        WorldState world,
        Settlement settlement,
        ArtifactKind kind,
        EntityId creatorId,
        EntityId religionId,
        int year,
        EntityId ownerId = default,
        TomeContents? contents = null)
    {
        EntityId id = world.Artifacts.NextId;

        TomeContents? written = contents;
        if (kind == ArtifactKind.Tome && written is null)
        {
            written = Tomes.Compose(
                world, settlement, world.Civilizations[settlement.CivilizationId], id, year);
        }

        // Named for its maker when it has one and for the place otherwise, which is how the two
        // kinds of famous object actually get their names. A written work is named for its
        // subject, so a Codex of a ruler is recognisable before its page is opened.
        string qualifier = written is not null
            ? world.NameOf(written.SubjectId)
            : creatorId.IsNone || !world.Figures.Contains(creatorId)
                ? settlement.Name
                : world.Figures[creatorId].Name;

        EntityId owner = LivingOwner(world, ownerId.IsNone ? creatorId : ownerId);

        var artifact = new Artifact(
            id,
            ArtifactKinds.Noun(kind, id.Index) + " of " + qualifier,
            kind,
            settlement.Id,
            year,
            owner)
        {
            CreatorId = creatorId,
            ReligionId = religionId,
            TomeContents = written,
        };

        world.Artifacts.Add(artifact);

        world.Chronicle.Record(
            year,
            EventKind.ArtifactCreated,
            id,
            obj: creatorId.IsNone ? owner : creatorId,
            location: settlement.Id,
            extra: owner.IsNone || owner == creatorId ? null : new[] { owner },
            data: Chronicle.Data(("kind", ArtifactKinds.Label(kind))));

        return artifact;
    }

    /// <summary>Every extant artifact kept at one settlement, in the order they were made.</summary>
    public static List<Artifact> HeldBy(WorldState world, EntityId settlementId)
    {
        var held = new List<Artifact>();

        foreach (Artifact artifact in world.Artifacts)
        {
            if (artifact.IsExtant && artifact.HolderId == settlementId) held.Add(artifact);
        }

        return held;
    }

    /// <summary>Every extant artifact claimed by one person, in the order they were made.</summary>
    public static List<Artifact> OwnedBy(WorldState world, EntityId figureId)
    {
        var owned = new List<Artifact>();
        if (figureId.IsNone) return owned;

        foreach (Artifact artifact in world.Artifacts)
        {
            if (artifact.IsExtant && artifact.OwnerId == figureId) owned.Add(artifact);
        }

        return owned;
    }

    /// <summary>
    /// Carries off what a sacked town was keeping.
    /// </summary>
    /// <remarks>
    /// Loot goes to the sacker's seat of government, and stays there — so a realm that spent three
    /// centuries sacking its neighbours ends up with a capital full of other people's regalia,
    /// which is both what happened historically and the most legible single page a chronicle of
    /// conquest can offer. The taking ruler claims what survives, when there is one.
    /// </remarks>
    public static void Loot(
        WorldState world, Settlement sacked, Civilization taker, int year, IRng rng)
    {
        if (taker.CapitalId.IsNone || !world.Settlements.Contains(taker.CapitalId)) return;

        Settlement seat = world.Settlements[taker.CapitalId];
        if (!seat.IsActive || seat.Id == sacked.Id) return;

        EntityId newOwner = LivingOwner(world, taker.CurrentRulerId);

        foreach (Artifact artifact in HeldBy(world, sacked.Id))
        {
            // Not everything survives a sack to be carried anywhere.
            if (!rng.Chance(0.65))
            {
                Lose(world, artifact, year, "in the sack");
                continue;
            }

            artifact.Transfer(seat.Id, newOwner, year, "taken as plunder");

            world.Chronicle.Record(
                year,
                EventKind.ArtifactTaken,
                artifact.Id,
                obj: taker.Id,
                location: seat.Id,
                extra: Extra(sacked.Id, newOwner));
        }
    }

    /// <summary>Yields the particular relic named in a victorious realm's terms of peace.</summary>
    public static void Claim(
        WorldState world, Artifact artifact, Civilization taker, War war, int year)
    {
        if (!artifact.IsExtant || artifact.Kind != ArtifactKind.Relic) return;

        // Nothing is handed over that the victor already holds. An army that took the relic when
        // it sacked the town keeps it; without this the same object arrives twice — "taken as
        // plunder" in the year of the sack and "claimed in peace" at the settlement, which reads
        // as two changes of hands and inflates every count drawn from the provenance.
        if (!world.Settlements.Contains(artifact.HolderId)
            || world.Settlements[artifact.HolderId].CivilizationId == taker.Id)
        {
            return;
        }

        if (taker.CapitalId.IsNone || !world.Settlements.Contains(taker.CapitalId)) return;

        Settlement seat = world.Settlements[taker.CapitalId];
        if (!seat.IsActive) return;

        EntityId newOwner = LivingOwner(world, taker.CurrentRulerId);
        artifact.Transfer(seat.Id, newOwner, year, "claimed in peace");

        world.Chronicle.Record(
            year,
            EventKind.ArtifactClaimed,
            artifact.Id,
            obj: taker.Id,
            location: seat.Id,
            extra: Extra(war.Id, newOwner));
    }

    /// <summary>Loses everything a settlement was holding — for a place nobody lives in any more.</summary>
    public static void LoseAll(WorldState world, Settlement settlement, int year, string cause)
    {
        foreach (Artifact artifact in HeldBy(world, settlement.Id))
        {
            Lose(world, artifact, year, cause);
        }
    }

    /// <summary>Loses one thing a settlement was holding, if it was holding anything.</summary>
    public static void LoseOne(
        WorldState world, Settlement settlement, int year, string cause, IRng rng)
    {
        List<Artifact> held = HeldBy(world, settlement.Id);
        if (held.Count == 0) return;

        Lose(world, held[rng.NextInt(held.Count)], year, cause);
    }

    /// <summary>
    /// Loses one thing a person owned, if they owned anything to lose.
    /// </summary>
    /// <remarks>
    /// For a robbery on the road. The location written is where the thing was being kept, because
    /// that is the last place anyone could name — nobody records the mile of road a chest went
    /// missing on, and the cause says the rest.
    /// </remarks>
    public static bool LoseCarried(
        WorldState world, Figure figure, int year, string cause, IRng rng)
    {
        List<Artifact> owned = OwnedBy(world, figure.Id);
        if (owned.Count == 0) return false;

        Lose(world, owned[rng.NextInt(owned.Count)], year, cause);
        return true;
    }

    /// <summary>
    /// Settles claims left on the dead, and passes a throne's treasures to whoever now sits it.
    /// </summary>
    /// <remarks>
    /// Runs after succession, so a crown made for a king who died this spring belongs to the
    /// heir by the time the year's artifacts are written. Personal goods of a private person go
    /// to a living child, then a spouse, then the treasury of the place that was keeping them.
    /// </remarks>
    public static void SettleEstates(WorldState world, int year)
    {
        IRng rng = world.Root.Fork("treasures.estate", year);

        foreach (Artifact artifact in world.Artifacts)
        {
            if (!artifact.IsExtant || artifact.OwnerId.IsNone) continue;
            if (!world.Figures.Contains(artifact.OwnerId)) continue;

            Figure owner = world.Figures[artifact.OwnerId];
            if (owner.IsAlive) continue;

            EntityId heir = HeirTo(world, owner, year);
            EntityId seat = SeatFor(world, heir, artifact.HolderId);
            string how = artifact.Kind == ArtifactKind.Regalia
                ? "inherited with the crown"
                : heir.IsNone
                    ? "returned to the treasury"
                    : "inherited";

            if (seat == artifact.HolderId && heir == artifact.OwnerId) continue;

            artifact.Transfer(seat, heir, year, how);

            if (!heir.IsNone)
            {
                world.Chronicle.Record(
                    year,
                    EventKind.ArtifactGiven,
                    artifact.Id,
                    obj: heir,
                    location: seat,
                    extra: new[] { owner.Id },
                    data: Chronicle.Data(("manner", how)));
            }
        }

        Gift(world, year, rng);
    }

    private static void Gift(WorldState world, int year, IRng rng)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            if (civilization.CurrentRulerId.IsNone
                || !world.Figures.Contains(civilization.CurrentRulerId))
            {
                continue;
            }

            Figure ruler = world.Figures[civilization.CurrentRulerId];
            if (!ruler.IsAlive) continue;

            double chance = 0.004 + (ruler.Disposition.Values.Piety * 0.008);
            if (!rng.Fork("civ", civilization.Id.ToDiscriminator()).Chance(chance)) continue;

            List<Artifact> owned = OwnedBy(world, ruler.Id);
            if (owned.Count == 0) continue;

            Artifact gift = owned[rng.NextInt(owned.Count)];
            if (gift.Kind == ArtifactKind.Regalia) continue;

            EntityId recipientId = GiftRecipient(world, civilization, gift, rng);
            if (recipientId.IsNone || recipientId == ruler.Id) continue;
            if (!world.Figures.Contains(recipientId) || !world.Figures[recipientId].IsAlive) continue;

            Figure recipient = world.Figures[recipientId];
            EntityId seat = SeatFor(world, recipient.Id, gift.HolderId);
            if (seat == gift.HolderId && recipient.Id == gift.OwnerId) continue;

            gift.Transfer(seat, recipient.Id, year, "given as a gift");

            world.Chronicle.Record(
                year,
                EventKind.ArtifactGiven,
                gift.Id,
                obj: recipient.Id,
                location: seat,
                extra: new[] { ruler.Id },
                data: Chronicle.Data(("manner", "as a gift")));
        }
    }

    private static EntityId GiftRecipient(
        WorldState world, Civilization civilization, Artifact gift, IRng rng)
    {
        if (gift.Kind == ArtifactKind.Tome || gift.Kind is ArtifactKind.Relic or ArtifactKind.Idol)
        {
            Figure? priest = Offices.HolderOf(world, civilization, OfficeKind.HighPriest);
            if (priest is not null) return priest.Id;
        }

        if (!civilization.RulingDynastyId.IsNone
            && world.Dynasties.Contains(civilization.RulingDynastyId))
        {
            Dynasty house = world.Dynasties[civilization.RulingDynastyId];
            var kin = new List<EntityId>();
            foreach (EntityId id in house.MemberIds)
            {
                if (id == civilization.CurrentRulerId || !world.Figures.Contains(id)) continue;
                if (world.Figures[id].IsAlive) kin.Add(id);
            }

            if (kin.Count > 0) return kin[rng.NextInt(kin.Count)];
        }

        return EntityId.None;
    }

    private static EntityId HeirTo(WorldState world, Figure owner, int year)
    {
        foreach (Civilization realm in world.Civilizations)
        {
            if (!realm.IsActive) continue;
            if (realm.CurrentRulerId.IsNone
                || realm.CurrentRulerId == owner.Id
                || !world.Figures.Contains(realm.CurrentRulerId)
                || !world.Figures[realm.CurrentRulerId].IsAlive)
            {
                continue;
            }

            bool ruledHere = false;
            foreach (EntityId id in realm.RulerIds)
            {
                if (id != owner.Id) continue;
                ruledHere = true;
                break;
            }

            if (ruledHere) return realm.CurrentRulerId;
        }

        foreach (EntityId childId in owner.ChildIds)
        {
            if (!world.Figures.Contains(childId)) continue;
            Figure child = world.Figures[childId];
            if (child.IsAlive && child.AgeIn(year) >= Succession.MajorityAge) return childId;
        }

        if (!owner.SpouseId.IsNone
            && world.Figures.Contains(owner.SpouseId)
            && world.Figures[owner.SpouseId].IsAlive)
        {
            return owner.SpouseId;
        }

        return EntityId.None;
    }

    private static EntityId SeatFor(WorldState world, EntityId ownerId, EntityId fallback)
    {
        if (!ownerId.IsNone && world.Figures.Contains(ownerId))
        {
            EntityId home = world.Figures[ownerId].ResidenceSettlementId;
            if (!home.IsNone
                && world.Settlements.Contains(home)
                && world.Settlements[home].IsActive)
            {
                return home;
            }

            if (world.Civilizations.Contains(world.Figures[ownerId].CivilizationId))
            {
                EntityId capital = world.Civilizations[world.Figures[ownerId].CivilizationId].CapitalId;
                if (!capital.IsNone
                    && world.Settlements.Contains(capital)
                    && world.Settlements[capital].IsActive)
                {
                    return capital;
                }
            }
        }

        if (!fallback.IsNone
            && world.Settlements.Contains(fallback)
            && world.Settlements[fallback].IsActive)
        {
            return fallback;
        }

        return fallback;
    }

    private static EntityId LivingOwner(WorldState world, EntityId candidate)
    {
        if (candidate.IsNone || !world.Figures.Contains(candidate)) return EntityId.None;
        return world.Figures[candidate].IsAlive ? candidate : EntityId.None;
    }

    private static EntityId[] Extra(EntityId first, EntityId second)
    {
        if (second.IsNone) return new[] { first };
        return new[] { first, second };
    }

    private static void Lose(WorldState world, Artifact artifact, int year, string cause)
    {
        EntityId where = artifact.HolderId;
        artifact.Lose(year, cause);

        world.Chronicle.Record(
            year,
            EventKind.ArtifactLost,
            artifact.Id,
            location: where,
            data: Chronicle.Data(("cause", cause)));
    }
}
