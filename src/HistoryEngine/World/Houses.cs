using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// Creating people, founding houses, and putting someone on a throne.
/// </summary>
/// <remarks>
/// The mutating counterpart to <see cref="Succession"/>, which only reads. Both the world builder
/// and the succession system need these, and having one copy is what guarantees that the first
/// ruler of a realm is assembled exactly like its hundredth — the alternative is a founding path
/// that quietly diverges from the running one and only shows up as an entity the viewer cannot
/// resolve.
/// </remarks>
public static class Houses
{
    /// <summary>Age range a figure who arrives already grown is given.</summary>
    private const int AdultMinAge = 20;

    private const int AdultMaxAge = 34;

    /// <summary>Adds a new person to the record.</summary>
    /// <remarks>
    /// No birth event: a figure created grown was born before anyone was writing this down, and
    /// events are appended in non-decreasing year order, so back-dating one is not available even
    /// if it were wanted. Only children born inside the chronicle get a birth.
    /// </remarks>
    public static Figure NewFigure(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Sex sex,
        int birthYear,
        EntityId birthSettlementId = default,
        Figure? mother = null,
        Figure? father = null)
    {
        EntityId id = world.Figures.NextId;
        EntityId bornAt = birthSettlementId.IsNone ? civilization.CapitalId : birthSettlementId;
        EntityId faithId = FaithFor(world, civilization, bornAt, mother, father);
        FaithCharacter? teaching = world.Religions.Contains(faithId)
            ? world.Religions[faithId].Character
            : null;

        var figure = new Figure(
            id,
            civilization.Id,
            culture.Id,
            world.Names.ForFigure(id, culture),
            sex,
            birthYear)
        {
            BirthSettlementId = bornAt,

            // Everyone starts where they were born. Office-holders who serve elsewhere overwrite
            // this; everyone else used to infer "at the capital" from the absence of an address,
            // which works until somebody needs to know where a figure with no office actually is.
            ResidenceSettlementId = bornAt,
            ReligionId = faithId,

            // Forked on the figure's own id, so a person's character cannot depend on how many
            // people were born before them — and, because a fork consumes nothing from its
            // parent, adding this drew no number away from any stream that already existed.
            // The faith tints the roll without consuming it, so a worldly sibling of a devout
            // one still drew the same numbers.
            Disposition = Disposition.Roll(
                culture, world.Root.Fork("disposition", id.ToDiscriminator()), teaching),
        };

        world.Figures.Add(figure);
        return figure;
    }

    /// <summary>
    /// The faith a new person is raised in: the birthplace first, then a parent, then the realm.
    /// </summary>
    /// <remarks>
    /// Place before blood, because congregations are settlements. A child born in a converted
    /// capital follows what that capital follows, even if a foreign parent kept another church;
    /// a child born before any faith has reached the town can still inherit one from a parent
    /// who already holds it. The realm is the last resort — the same answer
    /// <see cref="WorldState.FaithOf(Civilization)"/> already gives everyone else.
    /// </remarks>
    public static EntityId FaithFor(
        WorldState world,
        Civilization civilization,
        EntityId birthSettlementId,
        Figure? mother = null,
        Figure? father = null)
    {
        if (world.Settlements.Contains(birthSettlementId))
        {
            EntityId held = world.Settlements[birthSettlementId].ReligionId;
            if (!held.IsNone) return held;
        }

        if (mother is not null && !mother.ReligionId.IsNone) return mother.ReligionId;
        if (father is not null && !father.ReligionId.IsNone) return father.ReligionId;

        return civilization.StateReligionId;
    }

    /// <summary>
    /// Raises a new house around a founder who appears from outside the record.
    /// </summary>
    /// <remarks>
    /// The old pre-dynastic behaviour, kept deliberately and demoted to what it always honestly
    /// was: the fallback for when no claimant exists. A house that dies out is replaced by
    /// somebody the chronicle had not been following, which is exactly how that goes.
    /// </remarks>
    public static Figure FoundDynasty(
        WorldState world, Civilization civilization, Culture culture, int year, IRng rng)
    {
        Sex sex = culture.Succession == SuccessionLaw.Agnatic
            ? Sex.Male
            : rng.Chance(0.5) ? Sex.Male : Sex.Female;

        Figure founder = NewFigure(
            world, civilization, culture, sex, year - rng.NextInt(AdultMinAge, AdultMaxAge + 1));

        EntityId dynastyId = world.Dynasties.NextId;
        var house = new Dynasty(
            dynastyId,
            culture.Id,
            world.Names.ForDynasty(dynastyId, culture),
            year,
            founder.Id,
            civilization.Id);

        world.Dynasties.Add(house);
        founder.DynastyId = dynastyId;
        founder.Occupation = Occupation.Court;

        world.Chronicle.Record(
            year,
            EventKind.DynastyFounded,
            dynastyId,
            obj: founder.Id,
            location: civilization.CapitalId);

        return founder;
    }

    /// <summary>
    /// Raises a house around someone the chronicle already follows.
    /// </summary>
    /// <remarks>
    /// <para>The counterpart to <see cref="FoundDynasty"/> for a figure who took a throne rather
    /// than appearing to fill one. A town-chosen governor who seizes a seat, or who breaks their
    /// province away, already has a spouse and children; crowning them without a house would leave
    /// those children of no house, and the new line would die with the person the rising just
    /// installed.</para>
    ///
    /// <para>Does nothing when they already belong to a house — a cadet who secedes keeps theirs,
    /// which is how one family comes to hold two thrones. Adopts living descendants who have no
    /// house of their own, so the first generation a notable was already followed for becomes the
    /// first generation of the house.</para>
    /// </remarks>
    public static void RaiseHouse(
        WorldState world, Civilization civilization, Culture culture, Figure founder, int year)
    {
        if (!founder.DynastyId.IsNone) return;

        EntityId dynastyId = world.Dynasties.NextId;
        var house = new Dynasty(
            dynastyId,
            culture.Id,
            world.Names.ForDynasty(dynastyId, culture),
            year,
            founder.Id,
            civilization.Id);

        world.Dynasties.Add(house);
        founder.DynastyId = dynastyId;
        AdoptLine(world, house, founder);

        world.Chronicle.Record(
            year,
            EventKind.DynastyFounded,
            dynastyId,
            obj: founder.Id,
            location: civilization.CapitalId);
    }

    /// <summary>
    /// Folds a notable's already-recorded children into the house just raised around them.
    /// </summary>
    private static void AdoptLine(WorldState world, Dynasty house, Figure parent)
    {
        foreach (EntityId childId in parent.ChildIds)
        {
            if (!world.Figures.Contains(childId)) continue;

            Figure child = world.Figures[childId];
            if (!child.DynastyId.IsNone) continue;

            child.DynastyId = house.Id;
            house.MemberIds.Add(child.Id);
            AdoptLine(world, house, child);
        }
    }

    /// <summary>
    /// Puts a figure on a throne and records it.
    /// </summary>
    /// <param name="claim">
    /// How they came by it, in prose — "by right of birth", "by the election of the realm". The
    /// chronicle is far more readable when a succession says which rule produced it, and it is
    /// the only visible difference between two realms whose laws disagree.
    /// </param>
    public static void Enthrone(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Figure ruler,
        int year,
        string claim)
    {
        // An heir who has been living abroad comes home to take the throne, and their consort comes
        // with them — a court is where the crown is, and leaving the spouse in the realm they were
        // married in would strand the next generation's mother a kingdom away from its nursery.
        //
        // Unless the consort holds a crown of their own: a ruler who does not live in the realm
        // they rule has their death routed to the wrong throne, which empties one realm's seat and
        // freezes the other's for ever. Two sovereigns married to each other simply keep their own
        // courts, and the union between them is Milestone 6's business.
        ruler.CivilizationId = civilization.Id;
        ruler.ResidenceSettlementId = civilization.CapitalId;

        if (world.Figures.Contains(ruler.SpouseId))
        {
            Figure consort = world.Figures[ruler.SpouseId];
            if (!Succession.HoldsAThrone(world, consort))
            {
                consort.CivilizationId = civilization.Id;
                consort.ResidenceSettlementId = civilization.CapitalId;
            }
        }

        EntityId previousHouse = civilization.RulingDynastyId;
        if (!ruler.DynastyId.IsNone && previousHouse != ruler.DynastyId)
        {
            civilization.RulingDynastyId = ruler.DynastyId;

            if (!previousHouse.IsNone)
            {
                world.Chronicle.Record(
                    year, EventKind.DynastyAscended, ruler.DynastyId, obj: civilization.Id,
                    extra: new[] { ruler.Id });
            }
        }

        Number(world, civilization, ruler);

        // A crown supersedes whatever lesser office its holder was in. A governor who inherits
        // does not go on governing his town from the capital, a marshal who is crowned hands
        // the army to somebody else, and a regent who takes a throne of their own — especially
        // a foreign one — cannot keep governing a child in the realm they just left. Leaving
        // any of them open would have one person holding two posts the appointment system
        // would then decline to refill, and an office whose realm no longer matches its holder.
        ruler.EndOffice(OfficeKind.Marshal, year);
        ruler.EndOffice(OfficeKind.Governor, year);
        ruler.EndOffice(OfficeKind.Consort, year);
        ruler.EndOffice(OfficeKind.HighPriest, year);

        if (ruler.Holds(OfficeKind.Regent))
        {
            foreach (Civilization realm in world.Civilizations)
            {
                if (realm.RegentId != ruler.Id) continue;

                realm.RegentId = EntityId.None;
                break;
            }

            ruler.EndOffice(OfficeKind.Regent, year);
        }

        ruler.Offices.Add(
            new OfficeHolding(OfficeKind.Ruler, culture.RulerTitle, civilization.Id, year, null)
            {
                Claim = claim,
            });

        civilization.CurrentRulerId = ruler.Id;
        civilization.RulerSinceYear = year;
        civilization.RulerIds.Add(ruler.Id);

        Campaigns.NoteRuler(world, ruler, civilization, year);

        if (world.Dynasties.Contains(ruler.DynastyId))
        {
            world.Dynasties[ruler.DynastyId].RulerIds.Add(ruler.Id);
        }

        world.Chronicle.Record(
            year,
            EventKind.RulerCrowned,
            ruler.Id,
            obj: civilization.Id,
            location: civilization.CapitalId,
            extra: ruler.DynastyId.IsNone ? null : new[] { ruler.DynastyId },
            data: Chronicle.Data(
                ("title", culture.RulerTitle),
                ("claim", claim),
                ("age", ruler.AgeIn(year).ToString(CultureInfo.InvariantCulture))));

        Occupations.Sync(world, ruler, year);
    }

    /// <summary>
    /// Gives a new ruler their regnal number, and the first of their name theirs.
    /// </summary>
    /// <remarks>
    /// Called before the ruler joins <see cref="Civilization.RulerIds"/>, so the list it counts is
    /// exactly their predecessors. Distinct figures rather than list entries, since an elective
    /// realm can return the same person to office and they keep the number they already had.
    /// </remarks>
    private static void Number(WorldState world, Civilization civilization, Figure ruler)
    {
        if (ruler.RegnalNumber > 0) return;

        Figure? first = null;
        var counted = new List<EntityId>();

        foreach (EntityId id in civilization.RulerIds)
        {
            Figure past = world.Figures[id];

            if (past.Id == ruler.Id || counted.Contains(past.Id)) continue;
            if (!string.Equals(past.Name, ruler.Name, StringComparison.Ordinal)) continue;

            first ??= past;
            counted.Add(past.Id);
        }

        if (counted.Count == 0) return;

        // The first of a name becomes "the First" only once there is a second.
        if (first!.RegnalNumber == 0) first.RegnalNumber = 1;

        ruler.RegnalNumber = counted.Count + 1;
    }

    /// <summary>
    /// Ends a life: closes the title, widows the spouse, and records the death.
    /// </summary>
    /// <remarks>
    /// Vacating <see cref="Civilization.CurrentRulerId"/> here rather than leaving it to the
    /// succession system means a throne is empty the instant its holder dies, whatever order the
    /// systems happen to run in.
    /// </remarks>
    /// <param name="extra">
    /// Further entities to index the death under, besides the house and immediate family.
    /// Political murders pass a named suspect and any wider concern so the event appears on their
    /// pages; ordinary deaths need no caller-supplied additions.
    /// </param>
    /// <param name="data">
    /// Further display facts merged onto the obituary — a named suspect, a particular form.
    /// The age, cause and office are always written here and win if the caller repeats them.
    /// </param>
    public static void Die(
        WorldState world,
        Figure figure,
        int year,
        DeathCause cause,
        string? detail = null,
        IReadOnlyList<EntityId>? extra = null,
        DetMap<string, string>? data = null)
    {
        // Read while the marriage still exists. Death clears the live spouse link below, but the
        // relationships and memories it leaves behind are permanent parts of the survivors.
        List<Figure> bereaved = Succession.ImmediateFamily(world, figure);
        Undertakings.EndAtDeath(world, figure, year);
        Disputes.EndAtDeath(world, figure, year);
        Conspiracies.EndAtDeath(world, figure, year);

        figure.DeathYear = year;
        figure.DeathCause = cause;
        figure.DeathDetail = detail;

        // Asked before the offices are closed, because closing them is what death does to them and
        // the question is what this person was when it happened. Someone who governed a province
        // thirty years ago and died in bed a private citizen is a vital record; the marshal who
        // died in office left a vacancy, which is the next thing the chronicle has to explain.
        bool inPower = HeldPower(figure);
        string? style = inPower ? StyleOf(world, figure) : null;

        figure.EndAllOffices(year);
        Occupations.Sync(world, figure, year, died: true);

        EntityId survivingSpouse = EntityId.None;
        if (world.Figures.Contains(figure.SpouseId))
        {
            Figure spouse = world.Figures[figure.SpouseId];
            if (spouse.IsAlive) survivingSpouse = spouse.Id;

            spouse.SpouseId = EntityId.None;
            figure.SpouseId = EntityId.None;
        }

        if (world.Civilizations.Contains(figure.CivilizationId))
        {
            Civilization civilization = world.Civilizations[figure.CivilizationId];

            if (civilization.CurrentRulerId == figure.Id) civilization.CurrentRulerId = EntityId.None;
            if (civilization.RegentId == figure.Id) civilization.RegentId = EntityId.None;
        }

        // The realm slot is not narrated; it decides whose page the death lands on. Only those who
        // governed get it, because with dynasties the overwhelming majority of deaths are infants,
        // and indexing every one against the realm buries three centuries of its actual history
        // under its nurseries. A child's death belongs to their house's page and their own.
        bool governed = figure.Offices.Count > 0;
        EntityId realm = governed ? figure.CivilizationId : EntityId.None;

        var obituary = Chronicle.Data(
            ("age", figure.AgeIn(year).ToString(CultureInfo.InvariantCulture)),
            ("cause", detail ?? CauseLabel(cause)),
            ("familyVerb", FamilyDeathVerb(cause)));

        // What they were when they died, so that the deaths kept in the chronicle explain
        // themselves. "Thorgill died at the age of 78" is a line a reader skips; "Thorgill,
        // Warden of Karay, died at the age of 78" is the line before the one appointing his
        // successor, and the two now read as the sequence they always were.
        if (style is not null) obituary["office"] = style;

        if (data is not null)
        {
            foreach (KeyValuePair<string, string> pair in data)
            {
                if (!obituary.ContainsKey(pair.Key)) obituary[pair.Key] = pair.Value;
            }
        }

        world.Chronicle.Record(
            year,
            EventKind.FigureDied,
            figure.Id,
            obj: realm,
            extra: IndexedOn(figure, survivingSpouse, bereaved, extra),
            data: obituary,
            significance: inPower || IsExceptional(cause)
                ? Significance.Notable
                : Significance.Routine);

        LifeStories.Bereave(world, figure, bereaved, year, cause);
        Upbringings.OnDeath(world, figure, year);

        CloseHouseIfLast(world, figure, year);
    }

    /// <summary>
    /// The house first, then the surviving household and whoever else the death concerns, with
    /// none repeated. This is the chronology half of salient bereavement: a memory of a parent's
    /// death must lead to the death on the survivor's page rather than point at a fact hidden on
    /// somebody else's.
    /// </summary>
    private static EntityId[]? IndexedOn(
        Figure figure,
        EntityId survivingSpouse,
        IReadOnlyList<Figure> bereaved,
        IReadOnlyList<EntityId>? extra)
    {
        if (figure.DynastyId.IsNone
            && survivingSpouse.IsNone
            && bereaved.Count == 0
            && (extra is null || extra.Count == 0))
        {
            return null;
        }

        var ids = new List<EntityId>(2 + bereaved.Count + (extra?.Count ?? 0));

        void Add(EntityId id)
        {
            if (id.IsNone || id == figure.Id) return;

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) return;
            }

            ids.Add(id);
        }

        Add(figure.DynastyId);
        Add(survivingSpouse);

        for (int i = 0; i < bereaved.Count; i++) Add(bereaved[i].Id);

        if (extra is not null)
        {
            for (int i = 0; i < extra.Count; i++) Add(extra[i]);
        }

        return ids.Count == 0 ? null : ids.ToArray();
    }

    private static string FamilyDeathVerb(DeathCause cause) => cause switch
    {
        DeathCause.Battle or DeathCause.Assassination or DeathCause.Execution
            or DeathCause.Poisoning or DeathCause.Duel => "was slain",
        _ => "died",
    };

    /// <summary>
    /// Ends a house when the person just buried was the last of its blood.
    /// </summary>
    /// <remarks>
    /// Detected here, at the death, rather than when a throne next falls vacant. A house that has
    /// already lost power is never asked for an heir again, so leaving the check to succession
    /// means the ones that fade quietly — much the commonest way a family ends — are never recorded
    /// as having ended at all, and the export carries houses that are extinct in fact and
    /// standing in the data.
    /// </remarks>
    private static void CloseHouseIfLast(WorldState world, Figure figure, int year)
    {
        if (!world.Dynasties.Contains(figure.DynastyId)) return;

        Dynasty house = world.Dynasties[figure.DynastyId];
        if (house.IsExtinct) return;

        foreach (EntityId id in house.MemberIds)
        {
            if (world.Figures[id].IsAlive) return;
        }

        house.EndedYear = year;

        // A house that fell in its founding year has no span worth stating, and the template would
        // otherwise render "died out after 0 years".
        int span = year - house.FoundedYear;
        var data = Chronicle.Data(
            ("rulers", house.RulerIds.Count.ToString(CultureInfo.InvariantCulture)));

        if (span > 0) data["years"] = Chronicle.Years(span);

        world.Chronicle.Record(
            year, EventKind.DynastyEnded, house.Id, obj: figure.CivilizationId, data: data);
    }

    /// <summary>
    /// How a sitting officer is styled: "Warden of Karay", "Archon of Calatates".
    /// </summary>
    /// <remarks>
    /// Composed against the office's scope where it has one and its realm where it does not, which
    /// is the same distinction <see cref="Offices"/> draws when it grants the thing — a governorship
    /// is over a town, a marshalcy is over a realm. Returns null rather than a bare title for an
    /// office whose scope has since been destroyed, because "Warden of" is worse than nothing.
    /// </remarks>
    private static string? StyleOf(WorldState world, Figure figure)
    {
        for (int i = 0; i < figure.Offices.Count; i++)
        {
            OfficeHolding held = figure.Offices[i];
            if (held.ToYear is not null || held.Kind == OfficeKind.Consort) continue;

            EntityId over = held.ScopeId.IsNone ? held.CivilizationId : held.ScopeId;
            if (over.IsNone) return null;

            string name = world.NameOf(over);
            return string.IsNullOrEmpty(name) ? null : held.Title + " of " + name;
        }

        return null;
    }

    /// <summary>
    /// Whether a figure holds an office that governs something, right now.
    /// </summary>
    /// <remarks>
    /// <para>Consort is excluded, and that exclusion is the whole reason this is not simply
    /// <see cref="Figure.CurrentOffice"/>. A consort holds a title rather than a power: they are
    /// made one by a wedding that the chronicle has already recorded, they decide nothing, and
    /// counting them makes every royal marriage promote a second household's worth of births,
    /// deaths and matches into the narrative. Every other office is someone a ruler had to choose
    /// and whose vacancy the realm has to fill.</para>
    ///
    /// <para>Used for significance only. Nothing about the simulation reads it, and the realm slot
    /// on a death deliberately asks the wider question — whether they ever governed — because that
    /// one decides which page the record files under rather than whether it is history.</para>
    /// </remarks>
    public static bool HeldPower(Figure figure)
    {
        for (int i = 0; i < figure.Offices.Count; i++)
        {
            if (figure.Offices[i].ToYear is null && figure.Offices[i].Kind != OfficeKind.Consort)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a death is history rather than a vital record, on cause alone.
    /// </summary>
    /// <remarks>
    /// <para>Age and illness carry off the overwhelming majority of everyone and say nothing about
    /// the world: the figure's own record already holds the year and the cause, so the chronicle
    /// line is a duplicate. Everything else here is either violence or misfortune specific enough
    /// to be worth a line — an assassination, an execution, a death in childbed, a named pestilence
    /// reaching a named person.</para>
    ///
    /// <para>Deliberately not a judgement about who died. A ruler's death in bed is notable because
    /// they held office, which <see cref="Die"/> checks separately; a cadet's death by assassin is
    /// notable because someone killed them. Both tests are needed and neither subsumes the other.</para>
    /// </remarks>
    private static bool IsExceptional(DeathCause cause) =>
        cause is not (DeathCause.OldAge or DeathCause.Illness or DeathCause.Unknown);

    public static string CauseLabel(DeathCause cause) => cause switch
    {
        DeathCause.OldAge => "old age",
        DeathCause.Illness => "illness",
        DeathCause.Battle => "wounds taken in battle",
        DeathCause.Assassination => "assassination",
        DeathCause.Accident => "misadventure",
        DeathCause.Execution => "execution",
        DeathCause.Childbirth => "childbed",
        DeathCause.Plague => "plague",
        DeathCause.Disaster => "a disaster",
        DeathCause.Poisoning => "poisoning",
        DeathCause.Duel => "a quarrel settled with steel",
        _ => "unknown causes",
    };
}
