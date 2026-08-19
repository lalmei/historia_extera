namespace HistoryEngine.Entities;

/// <summary>
/// How a recorded person spends their life, when the chronicle is following them.
/// </summary>
/// <remarks>
/// <para>Distinct from <see cref="FigureOrigin"/>. Origin is the door they arrived through —
/// empty for anyone born into the record. Occupation is what they do once they are in it, and
/// everyone who lives to majority has one, including the children of a house.</para>
///
/// <para>The kinds line up with the offices a court actually fills, so a soldier is the person
/// a marshal's seat is looking for and a merchant is not. Sharing the vocabulary means the
/// appointment model can prefer a career without a second mapping every consumer would have
/// to keep honest.</para>
/// </remarks>
public enum Occupation
{
    /// <summary>Still a child, or not yet old enough for the chronicle to have asked.</summary>
    None = 0,

    /// <summary>Arms. The career a marshal is raised from.</summary>
    Soldiery = 1,

    /// <summary>The temple. The career a high priest is raised from.</summary>
    Clergy = 2,

    /// <summary>A standing figure of their own town. The career a governor is raised from.</summary>
    Townsfolk = 3,

    /// <summary>A craft or learned trade.</summary>
    Guild = 4,

    /// <summary>Trade on their own account.</summary>
    Merchant = 5,

    /// <summary>A court life: a dynast, a consort, a founder who arrived to take a throne.</summary>
    Court = 6,

    /// <summary>
    /// Holding a civic post. Distinct from the title on the holding: a merchant named governor
    /// is in office until they step down, then a merchant again.
    /// </summary>
    Official = 7,

    /// <summary>Letters: copying, composing, keeping accounts.</summary>
    Scribe = 8,
}
