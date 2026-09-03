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
    /// How long a household is followed after the office that raised it ends.
    /// </summary>
    /// <remarks>
    /// Twenty-five years is a child growing up, and that is the whole of the argument. Following a
    /// family only while the seat is held would make them vanish the year the holder dies, which is
    /// both wrong and useless: a local family's standing outlives the post that gave it to them,
    /// and the interval worth modelling is exactly the one in which the next holder could come from
    /// the same household. The same number therefore bounds <see cref="HeirTo"/>.
    /// </remarks>
    public const int GraceYears = 25;

    /// <summary>The claim recorded when a family keeps a seat it already held.</summary>
    /// <remarks>
    /// A constant because it is the only trace <see cref="FillMode.Customary"/> leaves in the
    /// export: a customary grant and an internal one both have no grantor, and the prose is what
    /// distinguishes a family keeping a seat from a body choosing its own.
    /// </remarks>
    public const string CustomaryClaim = "as the office has long run in their family";

    /// <summary>
    /// Whether this is an office somebody can be raised out of the population into.
    /// </summary>
    /// <remarks>
    /// A crown is inherited, a regency is delegated and a consort's style comes with a marriage.
    /// These three are the ones a court actually appoints to, and so the three that can raise a
    /// household that was not there before.
    /// </remarks>
    public static bool IsAppointed(OfficeKind kind) =>
        kind is OfficeKind.Marshal or OfficeKind.HighPriest or OfficeKind.Governor;

    /// <summary>
    /// Whether the chronicle is currently following this figure's household.
    /// </summary>
    /// <remarks>
    /// <para><b>One window, read by both halves of the model</b>, which is why it lives here rather
    /// than in either caller. <c>HouseholdSystem</c> asks it to decide whose marriages and children
    /// are worth recording; <see cref="HeirTo"/> asks it, through the same
    /// <see cref="GraceYears"/>, to decide whether a vacant seat still has a family attached to it.
    /// Two separate numbers would eventually disagree, and the disagreement has a definite shape: a
    /// person simultaneously too remote for the chronicle to marry off and close enough to inherit
    /// a governorship.</para>
    ///
    /// <para><b>Only those of no house.</b> A cadet made marshal is already followed, at whatever
    /// distance from the throne their birth put them, and promoting them to the head of a household
    /// would let an office pull a remote branch of a dynasty back into the nursery. That is a
    /// growth rate rather than a level shift, and it is the thing the attention budget exists to
    /// refuse.</para>
    /// </remarks>
    public static bool HeadsAHousehold(Figure figure, int year)
    {
        if (!figure.IsAlive || !figure.DynastyId.IsNone) return false;

        foreach (OfficeHolding held in figure.Offices)
        {
            if (!IsAppointed(held.Kind)) continue;
            if (held.ToYear is not int ended) return true;
            if (year - ended <= GraceYears) return true;
        }

        return false;
    }

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
        CampaignMemory? promotion = kind == OfficeKind.Marshal
            ? Campaigns.PromotionCause(holder)
            : null;
        if (promotion is not null && world.Battles.Contains(promotion.BattleId))
        {
            promotion.PromotionYear = year;
            claim += " after winning renown at " + world.NameOf(promotion.BattleId);
        }

        holder.Offices.Add(
            new OfficeHolding(kind, title, civilization.Id, year, null)
            {
                ScopeId = scope,
                GrantedBy = grantedBy,
                Claim = claim,
            });

        // A realm's marshal is its ranking soldier, whatever the ladder had got to before the
        // court reached for him. Silent, because the grant a line above has already said it.
        if (kind == OfficeKind.Marshal)
        {
            Ranks.Commission(world, civilization, culture, holder, year);
        }

        // An office over a place is where its holder now lives, which is what exposes them to
        // everything that happens there. Offices over a realm or a faith leave them at court.
        if (kind == OfficeKind.Governor && world.Settlements.Contains(scope))
        {
            Houses.Settle(world, holder, scope, ResidenceReason.Posting, year, withHousehold: true);
        }

        // Taking holy office is entering the church. A faithless courtier who is named high
        // priest does not stay faithless; a holder already of this faith is unchanged.
        if (kind == OfficeKind.HighPriest && !scope.IsNone)
        {
            holder.ReligionId = scope;
        }

        // A consort is made by the wedding, and the wedding has already been recorded a moment
        // earlier by the household system — so this line says nothing the one above it did not,
        // and there are as many of them as there are marriages into a crown. The appointments
        // that carry political weight are the ones a ruler had to decide: marshals, governors,
        // high priests. Those stay.
        world.Chronicle.Record(
            year,
            EventKind.OfficeGranted,
            holder.Id,
            obj: Subject(kind, civilization, scope),
            location: Seat(world, civilization, kind, scope),
            extra: Sidelight(kind, civilization, scope),
            data: Chronicle.Data(("office", title), ("claim", claim)),
            significance: kind == OfficeKind.Consort
                ? Significance.Routine
                : Significance.Notable);

        if (world.Figures.Contains(grantedBy) && grantedBy != holder.Id)
        {
            LifeStories.AddPatronage(
                world.Figures[grantedBy], holder, year, Seat(world, civilization, kind, scope));
        }

        if (holder.Background is { } background && background.IntroducedYear == year)
        {
            background.InstitutionId = BackgroundInstitution(kind, civilization, scope);
            background.SponsorId = grantedBy;
        }

        Occupations.Sync(world, holder, year);
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

        Undertakings.EndAtLossOfOffice(world, holder, kind, year);
        holder.EndOffice(kind, year);
        if (kind == OfficeKind.Governor) SendHome(world, holder, year);

        // Remembered, because losing an office badly is the sort of thing a court acts on later.
        holder.DisgracedYear = year;

        world.Chronicle.Record(
            year,
            EventKind.OfficeRevoked,
            holder.Id,
            obj: held.CivilizationId,
            data: Chronicle.Data(("office", held.Title), ("cause", cause)));

        if (world.Civilizations.Contains(held.CivilizationId))
        {
            EntityId rulerId = world.Civilizations[held.CivilizationId].CurrentRulerId;
            if (world.Figures.Contains(rulerId) && rulerId != holder.Id)
            {
                Figure ruler = world.Figures[rulerId];
                LifeStories.AddRivalry(
                    holder, ruler, year, EventKind.OfficeRevoked, world.ResidenceOf(holder), 0.52);
                LifeStories.Remember(
                    holder,
                    MemoryKind.Humiliation,
                    year,
                    EventKind.OfficeRevoked,
                    ruler.Id,
                    world.ResidenceOf(holder),
                    0.78);
                Disputes.Consider(
                    world,
                    holder,
                    ruler,
                    DisputeCause.OfficeRevoked,
                    EventKind.OfficeRevoked,
                    held.CivilizationId,
                    year);
            }
        }

        Occupations.Sync(world, holder, year);
    }

    /// <summary>Ends an office quietly: a lapse, a posting that no longer exists, a body that ended.</summary>
    public static void Lapse(WorldState world, Figure holder, OfficeKind kind, int year)
    {
        Undertakings.EndAtLossOfOffice(world, holder, kind, year);
        holder.EndOffice(kind, year);
        if (kind == OfficeKind.Governor) SendHome(world, holder, year);
        Occupations.Sync(world, holder, year, died: !holder.IsAlive);
    }

    /// <summary>
    /// Returns a figure to their realm's seat when the posting that moved them ends.
    /// </summary>
    /// <remarks>
    /// Their household goes with them, which is the point of recording where people live rather
    /// than only where they hold office: a governor recalled to court does not leave his wife and
    /// children in a provincial town to be counted among its casualties.
    /// </remarks>
    private static void SendHome(WorldState world, Figure holder, int year)
    {
        if (!world.Civilizations.Contains(holder.CivilizationId)) return;

        // The household rule this method used to own now lives in one place, with the move itself.
        Houses.Settle(
            world,
            holder,
            world.Civilizations[holder.CivilizationId].CapitalId,
            ResidenceReason.Recall,
            year,
            withHousehold: true);
    }

    /// <summary>
    /// Invents somebody local to fill a seat the court cannot.
    /// </summary>
    /// <remarks>
    /// <para>Of no house, deliberately, and that is still what bounds them — but the bound moved.
    /// A notable used to hold their office, die, and be replaced without ever entering a nursery,
    /// because <c>HouseholdSystem</c> refused to match anyone whose <see cref="Figure.DynastyId"/>
    /// is none. They now marry and have children, and what stops that compounding is
    /// <see cref="HeadsAHousehold"/>: the household is followed while the seat is held and for
    /// <see cref="GraceYears"/> after, and the children are recorded without being extended in
    /// turn. One spouse and a few children per seat is a level shift. A family that went on
    /// breeding families would be a growth rate.</para>
    ///
    /// <para>Staying out of the houses is what keeps that true. A notable married into a dynasty
    /// would put their children in a line of succession, and the line is ranked by proximity to a
    /// throne rather than by this window — so the bound would pass out of this file's hands.</para>
    ///
    /// <para>One per seat, so their number tracks the settlements above the governor threshold and
    /// the faiths in the world — neither of which compounds. Fewer than that, now: a seat with an
    /// heir is filled by <see cref="HeirTo"/> without inventing anybody at all.</para>
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

        EntityId bornAt = world.Settlements.Contains(residence) ? residence : civilization.CapitalId;

        Figure notable = Houses.NewFigure(
            world,
            civilization,
            culture,
            ClericSex(world, civilization, office, rng),
            year - rng.NextInt(min, max + 1),
            birthSettlementId: bornAt);

        notable.Origin = DoorInto(office);
        notable.Occupation = Occupations.ForOffice(office);
        notable.Background = new FigureBackground(
            year, bornAt, Upbringings.FamilyOf(notable.Occupation));

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

        if (!world.Religions[faithId].Character.Admits(figure.Sex)) return false;

        // A faith that forbids its clergy to marry does not hand its highest seat to a married
        // person. Checked here rather than left to the household roll, because the seat carries
        // Occupation.Clergy with it: without this, an appointment is a second door into orders
        // and the vow reaches nobody who came through it.
        if (figure.IsMarried && world.Religions[faithId].Character.CelibateClergy) return false;

        // A person of another church is not a candidate for this one's seat. The faithless
        // still are — taking the office is how they enter it.
        return figure.ReligionId.IsNone || figure.ReligionId == faithId;
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
    /// The child of this seat's last holder who is free to take it, if there is one.
    /// </summary>
    /// <remarks>
    /// <para>What makes <see cref="FillMode.Customary"/> reachable. It was unbuildable for as long
    /// as raised notables had no heirs — the third fill mode was designed, declared, and produced
    /// by nothing, because the people who filled most seats died childless by construction. A seat
    /// whose last holder left a grown child is a seat a local family expects to keep.</para>
    ///
    /// <para><b>Not the realm's succession law.</b> There is no crown here to partition and nobody
    /// to contest it, so the eldest who is grown and free takes it — which also means no number is
    /// drawn to decide who, and adding a child cannot shift an unrelated appointment.</para>
    ///
    /// <para>Walks the figure table for the same reason <see cref="HolderOf"/> does: office holders
    /// are a handful per realm, this is asked once per vacancy rather than once per year, and an
    /// index of former holders is a second place for the answer to be wrong.</para>
    /// </remarks>
    public static Figure? HeirTo(
        WorldState world, Civilization civilization, OfficeKind kind, EntityId scope, int year)
    {
        Figure? last = LastHolder(world, civilization, kind, scope, year);
        if (last is null) return null;

        Figure? heir = null;

        foreach (EntityId childId in last.ChildIds)
        {
            if (!world.Figures.Contains(childId)) continue;

            Figure child = world.Figures[childId];
            if (!Available(world, child, civilization, year)) continue;
            if (kind == OfficeKind.HighPriest && !EligibleCleric(world, civilization, child)) continue;

            if (heir is null || child.BirthYear < heir.BirthYear) heir = child;
        }

        return heir;
    }

    /// <summary>
    /// Whoever last held this exact seat, within living memory of it.
    /// </summary>
    /// <remarks>
    /// Bounded by <see cref="GraceYears"/>, so a seat empty for a century is not still owed to
    /// somebody's great-grandchild — and so that this and the household window can never disagree
    /// about whether a family is still attached to a post.
    /// </remarks>
    private static Figure? LastHolder(
        WorldState world, Civilization civilization, OfficeKind kind, EntityId scope, int year)
    {
        Figure? last = null;
        int latest = int.MinValue;

        foreach (Figure figure in world.Figures)
        {
            foreach (OfficeHolding held in figure.Offices)
            {
                if (held.Kind != kind || held.CivilizationId != civilization.Id) continue;
                if (held.ScopeId != scope) continue;
                if (held.ToYear is not int ended) continue;
                if (ended > year || year - ended > GraceYears) continue;

                // A family stripped of a seat does not keep it. Disgrace is the one ending that
                // says the court thought again about this household, and a dismissed governor's
                // son inheriting the post the following year would read as the model not noticing
                // what it had just recorded.
                if (figure.DisgracedYear == ended) continue;

                // Id breaks a tie, so two seats ended in one year cannot be ordered by however the
                // figure table happened to be walked.
                if (ended < latest) continue;
                if (ended == latest && last is not null && figure.Id.CompareTo(last.Id) >= 0) continue;

                latest = ended;
                last = figure;
            }
        }

        return last;
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

    /// <summary>The sitting governor of a settlement, if it has one.</summary>
    public static Figure? GovernorOf(WorldState world, Settlement settlement)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;

            OfficeHolding? held = figure.OpenOffice(OfficeKind.Governor);
            if (held is not null && held.ScopeId == settlement.Id) return figure;
        }

        return null;
    }

    /// <summary>What the office is held over, for the event's object slot.</summary>
    private static EntityId Subject(OfficeKind kind, Civilization civilization, EntityId scope) =>
        kind == OfficeKind.Governor ? scope : civilization.Id;

    private static EntityId BackgroundInstitution(
        OfficeKind kind, Civilization civilization, EntityId scope) =>
        kind is OfficeKind.Governor or OfficeKind.HighPriest && !scope.IsNone
            ? scope
            : civilization.Id;

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
