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
        int birthYear)
    {
        EntityId id = world.Figures.NextId;

        var figure = new Figure(
            id,
            civilization.Id,
            culture.Id,
            world.Names.ForFigure(id, culture),
            sex,
            birthYear)
        {
            BirthSettlementId = civilization.CapitalId,
        };

        world.Figures.Add(figure);
        return figure;
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

        world.Chronicle.Record(
            year,
            EventKind.DynastyFounded,
            dynastyId,
            obj: founder.Id,
            location: civilization.CapitalId);

        return founder;
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

        if (world.Figures.Contains(ruler.SpouseId))
        {
            Figure consort = world.Figures[ruler.SpouseId];
            if (!Succession.HoldsAThrone(world, consort)) consort.CivilizationId = civilization.Id;
        }

        EntityId previousHouse = civilization.RulingDynastyId;
        if (!ruler.DynastyId.IsNone && previousHouse != ruler.DynastyId)
        {
            civilization.RulingDynastyId = ruler.DynastyId;

            if (!previousHouse.IsNone)
            {
                world.Chronicle.Record(
                    year, EventKind.DynastyAscended, ruler.DynastyId, obj: civilization.Id);
            }
        }

        Number(world, civilization, ruler);
        ruler.Titles.Add(new TitleHolding(culture.RulerTitle, civilization.Id, year, null));

        civilization.CurrentRulerId = ruler.Id;
        civilization.RulerSinceYear = year;
        civilization.RulerIds.Add(ruler.Id);

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
    public static void Die(WorldState world, Figure figure, int year, DeathCause cause)
    {
        figure.DeathYear = year;
        figure.DeathCause = cause;
        figure.EndAllTitles(year);

        if (world.Figures.Contains(figure.SpouseId))
        {
            world.Figures[figure.SpouseId].SpouseId = EntityId.None;
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
        EntityId realm = figure.Titles.Count > 0 ? figure.CivilizationId : EntityId.None;

        world.Chronicle.Record(
            year,
            EventKind.FigureDied,
            figure.Id,
            obj: realm,
            extra: figure.DynastyId.IsNone ? null : new[] { figure.DynastyId },
            data: Chronicle.Data(
                ("age", figure.AgeIn(year).ToString(CultureInfo.InvariantCulture)),
                ("cause", CauseLabel(cause))));

        CloseHouseIfLast(world, figure, year);
    }

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

    public static string CauseLabel(DeathCause cause) => cause switch
    {
        DeathCause.OldAge => "old age",
        DeathCause.Illness => "illness",
        DeathCause.Battle => "wounds taken in battle",
        DeathCause.Assassination => "assassination",
        DeathCause.Accident => "misadventure",
        DeathCause.Execution => "execution",
        DeathCause.Childbirth => "childbed",
        _ => "unknown causes",
    };
}
