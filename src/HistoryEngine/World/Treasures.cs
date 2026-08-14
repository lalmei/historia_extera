using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// What happens to made things: where they are made, who takes them, and how they are lost.
/// </summary>
/// <remarks>
/// Shared for the same reason <see cref="Realms"/> is. An artifact leaves a settlement three
/// entirely different ways — carried off by an army, buried with a town nobody could feed any
/// longer, or burned in a disaster — and each is written by a different system. If each wrote
/// its own version, one of them would eventually forget to clear the holder or record the move,
/// and the symptom would be a treasure in two places at once.
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
        int year)
    {
        EntityId id = world.Artifacts.NextId;

        // Named for its maker when it has one and for the place otherwise, which is how the two
        // kinds of famous object actually get their names.
        string qualifier = creatorId.IsNone || !world.Figures.Contains(creatorId)
            ? settlement.Name
            : world.Figures[creatorId].Name;

        var artifact = new Artifact(
            id,
            ArtifactKinds.Noun(kind, id.Index) + " of " + qualifier,
            kind,
            settlement.Id,
            year)
        {
            CreatorId = creatorId,
            ReligionId = religionId,
        };

        world.Artifacts.Add(artifact);

        world.Chronicle.Record(
            year,
            EventKind.ArtifactCreated,
            id,
            obj: creatorId,
            location: settlement.Id,
            data: Chronicle.Data(("kind", ArtifactKinds.Label(kind))));

        return artifact;
    }

    /// <summary>Every extant artifact held by one settlement, in the order they were made.</summary>
    public static List<Artifact> HeldBy(WorldState world, EntityId settlementId)
    {
        var held = new List<Artifact>();

        foreach (Artifact artifact in world.Artifacts)
        {
            if (artifact.IsExtant && artifact.HolderId == settlementId) held.Add(artifact);
        }

        return held;
    }

    /// <summary>
    /// Carries off what a sacked town was keeping.
    /// </summary>
    /// <remarks>
    /// Loot goes to the sacker's seat of government, and stays there — so a realm that spent three
    /// centuries sacking its neighbours ends up with a capital full of other people's regalia,
    /// which is both what happened historically and the most legible single page a chronicle of
    /// conquest can offer.
    /// </remarks>
    public static void Loot(
        WorldState world, Settlement sacked, Civilization taker, int year, IRng rng)
    {
        if (taker.CapitalId.IsNone || !world.Settlements.Contains(taker.CapitalId)) return;

        Settlement seat = world.Settlements[taker.CapitalId];
        if (!seat.IsActive || seat.Id == sacked.Id) return;

        foreach (Artifact artifact in HeldBy(world, sacked.Id))
        {
            // Not everything survives a sack to be carried anywhere.
            if (!rng.Chance(0.65))
            {
                Lose(world, artifact, year, "in the sack");
                continue;
            }

            artifact.MoveTo(seat.Id, year, "taken as plunder");

            world.Chronicle.Record(
                year,
                EventKind.ArtifactTaken,
                artifact.Id,
                obj: taker.Id,
                location: seat.Id,
                extra: new[] { sacked.Id });
        }
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
