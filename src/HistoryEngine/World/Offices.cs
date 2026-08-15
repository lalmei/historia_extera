using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>How a seat came to be filled.</summary>
/// <remarks>
/// The substance of the appointment model, and what makes two realms' office histories read
/// unlike each other. A realm that mandates everything and one that lets its towns choose their
/// own headmen produce completely different chronicles from the same events.
/// </remarks>
public enum FillMode
{
    /// <summary>The ruler named a holder. Personal to them; it lapses when their reign does.</summary>
    Mandated = 0,

    /// <summary>The body chose its own. No grantor, and it holds until death.</summary>
    Internal = 1,

    /// <summary>It runs in a family. The last holder's heir takes it and the crown acquiesces.</summary>
    Customary = 2,
}

/// <summary>
/// Granting and ending the offices below a throne.
/// </summary>
/// <remarks>
/// The mutating counterpart to <see cref="Succession"/>, as <see cref="Houses"/> is for crowns.
/// Kept out of the yearly system for the same reason: the founding path and the running path must
/// assemble an office identically, or a colony's first governor differs from its hundredth in some
/// way that only shows up as an entity the viewer cannot resolve.
/// </remarks>
public static class Offices
{
    /// <summary>Youngest age at which someone may hold an office below the throne.</summary>
    public const int ServiceAge = Succession.MajorityAge;

    /// <summary>
    /// The age at which someone raised from the ordinary population takes each office.
    /// </summary>
    /// <remarks>
    /// <para>Career length, not a uniform guess. Every office used to recruit at 26–45 whatever it
    /// was, so a high priest and a town's headman were the same age on average and neither had
    /// done anything to get there. An office is the end of a career, and the band is how long that
    /// career took.</para>
    ///
    /// <para>A marshal has served: long enough to have been a soldier and been noticed, short
    /// enough to still take the field. A high priest has risen through a temple, which is the
    /// slowest ladder and the one where age is itself a qualification. A town's governor is
    /// somebody established in it — old enough to own something, young enough to be worth the
    /// crown's while.</para>
    /// </remarks>
    private static (int Min, int Max) CareerAge(OfficeKind office) => office switch
    {
        OfficeKind.Marshal => (32, 52),
        OfficeKind.HighPriest => (38, 62),
        OfficeKind.Governor => (30, 55),
        _ => (26, 45),
    };

    /// <summary>Which door into the record each office opens.</summary>
    private static FigureOrigin DoorInto(OfficeKind office) => office switch
    {
        OfficeKind.Marshal => FigureOrigin.Soldiery,
        OfficeKind.HighPriest => FigureOrigin.Clergy,
        OfficeKind.Governor => FigureOrigin.Townsfolk,
        _ => FigureOrigin.Unrecorded,
    };

    /// <summary>
    /// Grants an office and records it.
    /// </summary>
    /// <param name="scope">The settlement or faith held over, or None for an office over the realm.</param>
    /// <param name="grantedBy">Whoever appointed them, or None where the body chose its own.</param>
    public static void Grant(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Figure holder,
        OfficeKind kind,
        EntityId scope,
        EntityId grantedBy,
        string claim,
        int year)
    {
        string title = culture.TitleFor(kind, holder.Sex);

        holder.Offices.Add(
            new OfficeHolding(kind, title, civilization.Id, year, null)
            {
                ScopeId = scope,
                GrantedBy = grantedBy,
                Claim = claim,
            });

        // An office over a place is where its holder now lives, which is what exposes them to
        // everything that happens there. Offices over a realm or a faith leave them at court.
        if (kind == OfficeKind.Governor && world.Settlements.Contains(scope))
        {
            holder.ResidenceSettlementId = scope;
        }

        world.Chronicle.Record(
            year,
            EventKind.OfficeGranted,
            holder.Id,
            obj: Subject(kind, civilization, scope),
            location: Seat(world, civilization, kind, scope),
            extra: Sidelight(kind, civilization, scope),
            data: Chronicle.Data(("office", title), ("claim", claim)));
    }

    /// <summary>
    /// Ends an office because its holder failed in it, and records the disgrace.
    /// </summary>
    /// <remarks>
    /// The only ending that gets an event. A holder's death already has one, and an appointment
    /// lapsing with the reign that made it carries nothing the next grant does not — recording
    /// both ends of every office roughly doubles what this system writes to say nothing.
    /// </remarks>
    public static void Revoke(
        WorldState world, Figure holder, OfficeKind kind, string cause, int year)
    {
        OfficeHolding? held = holder.OpenOffice(kind);
        if (held is null) return;

        holder.EndOffice(kind, year);
        if (kind == OfficeKind.Governor) holder.ResidenceSettlementId = EntityId.None;

        // Remembered, because losing an office badly is the sort of thing a court acts on later.
        holder.DisgracedYear = year;

        world.Chronicle.Record(
            year,
            EventKind.OfficeRevoked,
            holder.Id,
            obj: held.CivilizationId,
            data: Chronicle.Data(("office", held.Title), ("cause", cause)));
    }

    /// <summary>Ends an office quietly: a lapse, a posting that no longer exists, a body that ended.</summary>
    public static void Lapse(WorldState world, Figure holder, OfficeKind kind, int year)
    {
        holder.EndOffice(kind, year);
        if (kind == OfficeKind.Governor) holder.ResidenceSettlementId = EntityId.None;
    }

    /// <summary>
    /// Invents somebody local to fill a seat the court cannot.
    /// </summary>
    /// <remarks>
    /// <para>Of no house, deliberately, and that is what bounds them. <c>HouseholdSystem</c> refuses
    /// to match anyone whose <see cref="Figure.DynastyId"/> is none, so an invented notable holds
    /// their office, dies, and is replaced without ever entering a nursery. The guard already
    /// existed for consorts married in from outside; this only has to avoid reaching around it.</para>
    ///
    /// <para>One per seat, so their number tracks the settlements above the governor threshold and
    /// the faiths in the world — neither of which compounds.</para>
    ///
    /// <para><b>They arrive with a life behind them.</b> The birth year is worked back from an age
    /// the office could plausibly be reached at, and the origin says which ladder they climbed, so
    /// a marshal raised from the ranks reads as a soldier who was noticed rather than as a name
    /// that appeared in the year it was needed. See <see cref="CareerAge"/>.</para>
    /// </remarks>
    public static Figure Notable(
        WorldState world,
        Civilization civilization,
        Culture culture,
        OfficeKind office,
        EntityId residence,
        int year,
        IRng rng)
    {
        (int min, int max) = CareerAge(office);

        Figure notable = Houses.NewFigure(
            world,
            civilization,
            culture,
            ClericSex(world, civilization, office, rng),
            year - rng.NextInt(min, max + 1));

        notable.Origin = DoorInto(office);

        // Born where they served, so far as the record goes. A governor is a figure of their own
        // town and a marshal or a high priest of the realm's seat, which is where the army musters
        // and the temple that matters stands.
        if (world.Settlements.Contains(residence))
        {
            notable.BirthSettlementId = residence;
            notable.ResidenceSettlementId = residence;
        }

        return notable;
    }

    /// <summary>
    /// The sex an invented office-holder is born with.
    /// </summary>
    /// <remarks>
    /// A faith that admits only men or only women to holy office is the one case where this is
    /// not a coin toss. Other offices stay even, which is what keeps a marshal's sex a fact of
    /// the person rather than of the post.
    /// </remarks>
    private static Sex ClericSex(
        WorldState world, Civilization civilization, OfficeKind office, IRng rng)
    {
        if (office != OfficeKind.HighPriest) return rng.Chance(0.5) ? Sex.Male : Sex.Female;

        EntityId faithId = world.FaithOf(civilization);
        if (faithId.IsNone || !world.Religions.Contains(faithId))
        {
            return rng.Chance(0.5) ? Sex.Male : Sex.Female;
        }

        return world.Religions[faithId].Character.ClericSex(rng);
    }

    /// <summary>Whether this figure may hold holy office in the realm's current faith.</summary>
    public static bool EligibleCleric(WorldState world, Civilization civilization, Figure figure)
    {
        EntityId faithId = world.FaithOf(civilization);
        if (faithId.IsNone || !world.Religions.Contains(faithId)) return true;

        return world.Religions[faithId].Character.Admits(figure.Sex);
    }

    /// <summary>Whether this figure is free to take an office.</summary>
    /// <remarks>
    /// One post at a time. A cadet who is already a marshal is not also available to govern a
    /// town four hundred miles away, and permitting it would concentrate every office in a realm
    /// on whichever two dynasts the line happened to leave idle.
    /// </remarks>
    public static bool Available(WorldState world, Figure figure, Civilization civilization, int year)
    {
        if (!figure.IsAlive) return false;
        if (figure.CivilizationId != civilization.Id) return false;
        if (figure.AgeIn(year) < ServiceAge) return false;
        if (figure.CurrentOffice is not null) return false;

        return figure.Id != civilization.CurrentRulerId && figure.Id != civilization.RegentId;
    }

    /// <summary>
    /// The court's candidates for a mandated office, nearest the throne last.
    /// </summary>
    /// <remarks>
    /// <b>Standing below the front of the line is a qualification, not a disqualification.</b> The
    /// heir is needed at home; a fourth son is precisely who gets an army or a colony. That
    /// inversion is why offices cost this engine nothing: the same cadets the attention budget
    /// already breeds and then has nothing to do with become the candidate pool, at no addition to
    /// the figure table.
    /// </remarks>
    public static List<Figure> Courtiers(
        WorldState world, Civilization civilization, int year)
    {
        var candidates = new List<Figure>();
        List<Figure> line = Succession.Kin(world, civilization);

        // Skipping index 0 keeps the presumed heir out of harm's way and out of the provinces.
        for (int i = 1; i < line.Count; i++)
        {
            if (Available(world, line[i], civilization, year)) candidates.Add(line[i]);
        }

        return candidates;
    }

    /// <summary>
    /// The living holder of a realm-wide office, if it has one.
    /// </summary>
    /// <remarks>
    /// The lookup every consumer goes through, so "does this realm have a marshal" is asked one
    /// way. Walks the figure table rather than keeping an index on the civilization: office
    /// holders are a handful per realm, the walk is once per battle rather than once per year,
    /// and an index is a second place for the answer to be wrong.
    /// </remarks>
    public static Figure? HolderOf(WorldState world, Civilization civilization, OfficeKind kind)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;

            OfficeHolding? held = figure.OpenOffice(kind);
            if (held is not null && held.CivilizationId == civilization.Id) return figure;
        }

        return null;
    }

    /// <summary>What the office is held over, for the event's object slot.</summary>
    private static EntityId Subject(OfficeKind kind, Civilization civilization, EntityId scope) =>
        kind == OfficeKind.Governor ? scope : civilization.Id;

    /// <summary>Where the granting happened.</summary>
    private static EntityId Seat(
        WorldState world, Civilization civilization, OfficeKind kind, EntityId scope) =>
        kind == OfficeKind.Governor && world.Settlements.Contains(scope)
            ? scope
            : civilization.CapitalId;

    /// <summary>
    /// The entity the event should also land on, beyond its three named slots.
    /// </summary>
    /// <remarks>
    /// A high priest's appointment belongs on the faith's page as much as the realm's, and a
    /// governorship on the realm's as much as the town's — but the prose reads better naming the
    /// realm in one case and the town in the other, so the other half travels here.
    /// </remarks>
    private static EntityId[]? Sidelight(
        OfficeKind kind, Civilization civilization, EntityId scope) => kind switch
    {
        OfficeKind.HighPriest when !scope.IsNone => new[] { scope },
        OfficeKind.Governor => new[] { civilization.Id },
        _ => null,
    };
}
