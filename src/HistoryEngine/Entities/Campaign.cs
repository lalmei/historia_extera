using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// How a recorded person stood in a war or an engagement. Explicit values — part of the export.
/// </summary>
public enum CampaignRole
{
    /// <summary>Led the army that took the field.</summary>
    Commanded = 0,

    /// <summary>Took the field as a soldier, without holding the command.</summary>
    Fought = 1,

    /// <summary>Ruled a belligerent while the war ran.</summary>
    Ruled = 2,

    /// <summary>Lived in a settlement while it was invested.</summary>
    EnduredSiege = 3,
}

/// <summary>
/// One war or engagement this person is remembered for.
/// </summary>
/// <remarks>
/// Written when they were present, then settled when the thing ended — the same pattern as an
/// office whose <c>ToYear</c> fills in later. A siege and a war both last past the day they
/// began, and the chronicle cannot know on that day whether they were triumphant.
/// </remarks>
public sealed class CampaignMemory
{
    public CampaignMemory(
        EntityId warId,
        EntityId battleId,
        EntityId sideId,
        int year,
        CampaignRole role)
    {
        WarId = warId;
        BattleId = battleId;
        SideId = sideId;
        Year = year;
        Role = role;
    }

    public EntityId WarId { get; }

    /// <summary>The engagement, or <see cref="EntityId.None"/> for a war they led rather than a battle they stood in.</summary>
    public EntityId BattleId { get; }

    /// <summary>The realm they stood with — the army they marched in, or the throne they held.</summary>
    public EntityId SideId { get; }

    public int Year { get; }

    public CampaignRole Role { get; }

    /// <summary>Whether their side prevailed. Null while the war or siege is still open, and after a stalemate.</summary>
    public bool? Triumphant { get; set; }
}
