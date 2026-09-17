using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// Who made a thing, out of the craftsmen a town already holds.
/// </summary>
/// <remarks>
/// <para><b>An object is the most durable thing this engine keeps.</b> It outlives its owner,
/// changes hands, is looted, gifted and written about; all of that provenance used to hang off an
/// origin of a town and a year, because the one id an artifact had for a person held the patron.
/// A named craftsman at the head of that chain is the cheapest fame there is, and it is the whole
/// point of giving guildsmen trades — see <see cref="Crafts"/>.</para>
///
/// <para><b>The maker is found here, never invented here.</b> This asks the settlement that
/// produced the object whether it holds a living adult of the right craft, and takes the answer.
/// It does not raise one, and it does not fall back to a craftsman from somewhere else.</para>
///
/// <para><b>Finding and raising are kept apart on purpose.</b> <see cref="Levies"/> answers the
/// other half — when a town could have held the trade and the record simply missed the man — and
/// it calls this first, always, so a town that holds a goldsmith uses him rather than acquiring a
/// rival. Keeping the two in one place would make it impossible to say which of the two answers a
/// given object got, and the distinction is the whole provenance question: a maker who was already
/// in the record is a different kind of fact from a maker the record went and found.</para>
///
/// <para><b>Anonymity is still a result, not a failure.</b> A jewel out of a town with no
/// goldsmith and no wealth to keep one names nobody, because the levy is gated on the same
/// <see cref="Crafts.Supports"/> the rest of the model is. What changed is only which towns are
/// silent: the ones that could not have held the trade, rather than the ones whose craftsmen were
/// never written down.</para>
///
/// <para><b>Nothing rolls.</b> A settlement's craftsman of a trade is its lowest-id living adult
/// practising it, which is the same rule <see cref="Tomes"/> uses to find a town's scribe. Adding
/// a draw here would make who made a crown depend on how many objects were made before it in the
/// same year, and the whole point of the id is that it does not.</para>
/// </remarks>
public static class Makers
{
    /// <summary>
    /// The crafts that could have produced each kind, best claim first.
    /// </summary>
    /// <remarks>
    /// <para>More than one where the trades genuinely overlap: a blade came off a smith's anvil or
    /// an armourer's, and a town that holds both is a town where the armourer made it, because a
    /// weapon good enough to be written down is not the village smith's ordinary work.</para>
    ///
    /// <para><b>A relic has no maker, and that is not an omission.</b> A relic is a thing that
    /// became holy, not a thing that was manufactured — the shroud, the bone, the censer that stood
    /// somewhere long enough to matter. Naming a craftsman for one would be the same error this
    /// whole change exists to undo, one step further along.</para>
    ///
    /// <para><b>A tome is absent because a tome already has an author.</b> Its creator is set by
    /// <see cref="Tomes"/> and read back as a sage by <see cref="HolySites"/>; authorship is that
    /// system's model and this one does not touch it.</para>
    /// </remarks>
    public static Craft[] CraftsFor(ArtifactKind kind) => kind switch
    {
        // Gold and silver work, whoever paid for it.
        ArtifactKind.Regalia => Goldwork,
        ArtifactKind.Jewel => Goldwork,

        // Iron. The armourer first where a town holds both.
        ArtifactKind.Weapon => Edged,
        ArtifactKind.Armor => Plate,

        // Cloth.
        ArtifactKind.Clothing => Cloth,

        // Carved or cast: stone first, then timber, then the potter's clay.
        ArtifactKind.Idol => Carved,

        _ => Array.Empty<Craft>(),
    };

    private static readonly Craft[] Goldwork = { Craft.Goldsmith };
    private static readonly Craft[] Edged = { Craft.Armourer, Craft.Smith };
    private static readonly Craft[] Plate = { Craft.Armourer };
    private static readonly Craft[] Cloth = { Craft.Weaver };
    private static readonly Craft[] Carved = { Craft.Mason, Craft.Carpenter, Craft.Potter };

    /// <summary>
    /// The craftsman of a town who made this kind of thing, or none.
    /// </summary>
    public static EntityId Find(
        EntityId settlementId,
        ArtifactKind kind,
        IReadOnlyDictionary<(EntityId Town, Craft Trade), Figure> guildsmen)
    {
        foreach (Craft craft in CraftsFor(kind))
        {
            if (guildsmen.TryGetValue((settlementId, craft), out Figure? maker))
            {
                return maker.Id;
            }
        }

        return EntityId.None;
    }

    /// <summary>
    /// Every town's craftsman of each trade, built once a year and read by every object made in it.
    /// </summary>
    /// <remarks>
    /// Lowest id wins, as it does for the scribe a town writes with. Built in one pass rather than
    /// scanned per settlement, because creation is rare and the figure list is not.
    /// </remarks>
    public static Dictionary<(EntityId Town, Craft Trade), Figure> Guildsmen(WorldState world, int year)
    {
        var found = new Dictionary<(EntityId, Craft), Figure>();

        foreach (Figure figure in world.Figures)
        {
            if (figure.Craft == Craft.None) continue;
            if (!figure.IsAlive || figure.ResidenceSettlementId.IsNone) continue;
            if (figure.AgeIn(year) < Succession.MajorityAge) continue;

            var key = (figure.ResidenceSettlementId, figure.Craft);
            if (found.TryGetValue(key, out Figure? standing)
                && standing.Id.CompareTo(figure.Id) <= 0)
            {
                continue;
            }

            found[key] = figure;
        }

        return found;
    }
}
