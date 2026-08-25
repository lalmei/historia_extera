using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>How a notable figure's life ended. Explicit values — part of the export format.</summary>
public enum DeathCause
{
    Unknown = 0,
    OldAge = 1,
    Illness = 2,
    Battle = 3,
    Assassination = 4,
    Accident = 5,
    Execution = 6,
    Childbirth = 7,
    Plague = 8,
    Disaster = 9,
    Poisoning = 10,

    /// <summary>Killed by a named person in a quarrel that both had agreed to settle.</summary>
    /// <remarks>
    /// Distinct from <see cref="Assassination"/>, which is done to someone who did not consent to
    /// meet, and from <see cref="Execution"/>, which a realm carries out. The whole point of the
    /// quarrel model is that this death has two people, a cause and a record of the climb to it.
    /// </remarks>
    Duel = 11,
}

/// <summary>
/// Which half of a marriage a figure can be.
/// </summary>
/// <remarks>
/// Modelled because succession law is the sharpest way one culture differs from another in a
/// chronicle, and every historical succession law is phrased in terms of sex — agnatic, male
/// preference, absolute. Without it there is one inheritance rule and every realm follows it.
/// </remarks>
public enum Sex
{
    Female = 0,
    Male = 1,
}

/// <summary>
/// What a figure was before the chronicle started following them. Explicit values — part of the
/// export format.
/// </summary>
/// <remarks>
/// <para>Only carries information for people raised <em>into</em> the record rather than born into
/// it. A dynast's origin is their house and a consort's is the marriage that brought them in; both
/// are already recorded, in more detail than this could add, so they stay
/// <see cref="Unrecorded"/>.</para>
///
/// <para>What it is for is the other path into the record. An office can be filled from the
/// ordinary population, and before this the person who arrived that way had no life at all: they
/// materialised at a plausible-looking age with nothing behind it. A marshal raised from the ranks
/// and a high priest risen through a temple are different people who arrive by different doors,
/// and saying which is the difference between a figure and a placeholder.</para>
/// </remarks>
public enum FigureOrigin
{
    /// <summary>Born into the record: a house, or a marriage into one.</summary>
    Unrecorded = 0,

    /// <summary>Rose through an army.</summary>
    Soldiery = 1,

    /// <summary>Rose through a temple.</summary>
    Clergy = 2,

    /// <summary>A standing figure of their own town.</summary>
    Townsfolk = 3,

    /// <summary>Rose through a guild.</summary>
    Guild = 4,

    /// <summary>Rose through a merchant house.</summary>
    Merchant = 5,
}

/// <summary>
/// The offices a figure can hold. Explicit values — part of the export format.
/// </summary>
/// <remarks>
/// <para><b>The kind is what systems read; the title is what the chronicle says.</b> One culture's
/// Marshal is another's Strategos and another's Warlord, and that is the cheapest character this
/// engine buys — but a system asking "does this realm have a marshal" must not be asking about a
/// string.</para>
///
/// <para>The split is load-bearing rather than tidy. Before it, a reign was identified by
/// <c>titles.find(t =&gt; t.title !== 'Regent')</c> in the viewer and by the same comparison in the
/// test suite. Neither breaks loudly when a second non-ruling office exists; both just quietly
/// start returning marshalships.</para>
/// </remarks>
public enum OfficeKind
{
    Ruler = 0,
    Regent = 1,
    Consort = 2,
    Marshal = 3,
    HighPriest = 4,
    Governor = 5,
    GuildMaster = 6,
    Merchant = 7,
    Noble = 8,
}

/// <summary>An office held over a span of years.</summary>
/// <param name="CivilizationId">The realm the office sits in. Never <see cref="EntityId.None"/>.</param>
/// <param name="ToYear">Null while the office is still held.</param>
/// <remarks>
/// The four positional members are what every office has. The rest are init-only because a crown
/// has no scope beyond its realm and nobody grants it — where an office does have those, they are
/// the whole difference between two governorships that are otherwise identical rows.
/// </remarks>
public sealed record OfficeHolding(
    OfficeKind Kind,
    string Title,
    EntityId CivilizationId,
    int FromYear,
    int? ToYear)
{
    /// <summary>The settlement or faith held over, where the office is over one.</summary>
    public EntityId ScopeId { get; init; } = EntityId.None;

    /// <summary>
    /// Whoever granted it, or <see cref="EntityId.None"/> where nobody did.
    /// </summary>
    /// <remarks>
    /// None is not missing data. A body that chose its own — a town's notables, a faith's clergy —
    /// has no grantor, and that is exactly what distinguishes it from a crown appointment.
    /// </remarks>
    public EntityId GrantedBy { get; init; } = EntityId.None;

    /// <summary>
    /// How they came by it, in prose: "by the king's mandate", "by the town's own council".
    /// </summary>
    /// <remarks>
    /// Borrowed from <see cref="World.Houses.Enthrone"/> deliberately. A coronation reads far
    /// better for saying which rule produced it, and an appointment is the same.
    /// </remarks>
    public string? Claim { get; init; }
}

/// <summary>
/// A notable person.
/// </summary>
/// <remarks>
/// Only leaders and their houses are simulated — the brief's deliberate choice, and the reason
/// a few centuries can run in seconds. Ordinary people exist only as
/// <see cref="Settlement.Population"/>.
///
/// <para>Figures are never deleted on death; they gain a <see cref="DeathYear"/>. A dead
/// king is exactly the sort of entity the viewer needs to keep resolving, since most of the
/// events referencing him are written after he dies.</para>
///
/// <para><b>Parents are named, not listed.</b> <see cref="MotherId"/> and
/// <see cref="FatherId"/> rather than a two-element collection, because inheritance asks
/// which parent specifically: a house passes down the father's line unless there is no
/// father's house to pass, and a list would force every caller to guess which end is which.</para>
/// </remarks>
public sealed class Figure
{
    public Figure(
        EntityId id,
        EntityId civilizationId,
        EntityId cultureId,
        string name,
        Sex sex,
        int birthYear)
    {
        Id = id;
        CivilizationId = civilizationId;
        CultureId = cultureId;
        Name = name;
        Sex = sex;
        BirthYear = birthYear;
        Offices = new List<OfficeHolding>();
        Campaigns = new List<CampaignMemory>();
        Journeys = new List<Journey>();
        Bonds = new List<FigureBond>();
        Memories = new List<SalientMemory>();
        Injuries = new List<FigureInjury>();
        Undertakings = new List<FigureUndertaking>();
        Disputes = new List<FigureDispute>();
        Plots = new List<FigurePlot>();
        Observations = new List<SkyObservation>();
        Claims = new List<SkyClaim>();
        ChildIds = new List<EntityId>();
        SpouseIds = new List<EntityId>();
    }

    public EntityId Id { get; }

    /// <summary>
    /// The realm this figure lives in.
    /// </summary>
    /// <remarks>Mutable: marrying abroad moves one partner, and inheriting a throne brings them home.</remarks>
    public EntityId CivilizationId { get; set; }

    public EntityId CultureId { get; }

    /// <summary>
    /// The faith this figure holds, or <see cref="EntityId.None"/> if none has reached them yet.
    /// </summary>
    /// <remarks>
    /// <para>Personal, not congregational. A settlement converts as a body; a person keeps what
    /// they were raised in, and may follow something their town no longer does. That is the
    /// whole reason this is a field rather than a lookup: if it always matched the residence,
    /// it would be denormalised data, and a high priest of a fading church would be
    /// indistinguishable from their neighbours.</para>
    ///
    /// <para>Assigned in <see cref="World.Houses.NewFigure"/> from the birthplace, then the
    /// parents, then the realm. Figures born before any faith existed pick one up later, when
    /// their residence first has one — see the religion tick — without rewriting the
    /// disposition already rolled. Conversion of a town does not convert its people.</para>
    /// </remarks>
    public EntityId ReligionId { get; set; } = EntityId.None;

    /// <summary>The personal name. See <see cref="FullName"/> for how a ruler is styled.</summary>
    public string Name { get; set; }

    /// <summary>
    /// Which holder of this name in this realm, or zero for someone who needs no distinguishing.
    /// </summary>
    /// <remarks>
    /// <para>Milestone 5 took a world from eighty named people to nearly a thousand, and names are a
    /// pure function of entity id with no uniqueness guarantee — so within a single culture the
    /// same name now recurs constantly. In a long run nearly half of all reigns belong to a realm
    /// that has had another ruler of the same name, which makes a line of succession genuinely
    /// unreadable.</para>
    ///
    /// <para>The fix is the one every real chronicle uses. Numbering is assigned at accession from
    /// the realm's own list of past rulers, so it depends on who ruled rather than on the order
    /// names were requested — the property <see cref="Naming.INameGenerator"/> exists to protect.
    /// The first of a name is numbered retroactively when the second appears, exactly as
    /// historians do it, which costs nothing because events carry ids and resolve names when they
    /// are rendered.</para>
    /// </remarks>
    public int RegnalNumber { get; set; }

    /// <summary>How the chronicle styles this figure: the name, with a numeral if one is needed.</summary>
    public string FullName => RegnalNumber >= 2 ? Name + " " + Numeral(RegnalNumber) : Name;

    public Sex Sex { get; }

    public int BirthYear { get; }

    public int? DeathYear { get; set; }

    public DeathCause DeathCause { get; set; } = DeathCause.Unknown;

    /// <summary>
    /// The particular form the cause took, when the system that killed this figure knows it.
    /// </summary>
    /// <remarks>
    /// The enum is the stable, filterable category; this is the chronicle's human detail —
    /// "the Speckled Fever", "wildfire", or "a riding accident". Natural deaths leave it empty
    /// and use the category's ordinary label.
    /// </remarks>
    public string? DeathDetail { get; set; }

    public bool IsAlive => DeathYear is null;

    /// <summary>
    /// The house this figure belongs to by blood, or <see cref="EntityId.None"/> for someone
    /// married in from outside the recorded houses.
    /// </summary>
    /// <remarks>
    /// Blood membership, not household membership — a consort keeps whatever house they were born
    /// into. That distinction is the whole basis of succession: only a house's blood can inherit
    /// its claims, which is what makes a house able to die out while its widows live on.
    /// </remarks>
    public EntityId DynastyId { get; set; } = EntityId.None;

    /// <summary>
    /// This person's own inclinations, as distinct from their culture's.
    /// </summary>
    /// <remarks>
    /// Rolled for everyone at birth and consulted only for those who come to govern — which is
    /// cheaper than it sounds, since it is derived from a fork on the figure's own id and so
    /// costs nothing until read. Assigned in <see cref="World.Houses.NewFigure"/>; the neutral
    /// default exists so that no code path can meet a figure without one.
    /// </remarks>
    public Disposition Disposition { get; init; } = Disposition.Neutral;

    /// <summary>
    /// How they spend their life, once the chronicle is following them.
    /// </summary>
    /// <remarks>
    /// <para>Empty until majority. A child has no trade, and inventing one at birth would
    /// pretend the chronicle knew a fact it does not. Raised notables arrive already grown
    /// and take the career the office implies; children of a recorded household choose from
    /// their disposition the year they come of age. See <see cref="World.Occupations"/>.</para>
    ///
    /// <para>Kept on the figure rather than derived from offices, because most people the
    /// chronicle follows never hold one, and the whole point of recording a governor's
    /// children is that they have a life the next vacancy can draw from.</para>
    /// </remarks>
    public Occupation Occupation { get; set; } = Occupation.None;

    /// <summary>
    /// The career they return to when an office ends, if they live. Empty until a posting
    /// actually changed what they did, and cleared when they have gone back.
    /// </summary>
    public Occupation PriorOccupation { get; set; } = Occupation.None;

    /// <summary>Every office this figure has ever held, in the order they were granted.</summary>
    public List<OfficeHolding> Offices { get; }

    /// <summary>
    /// Wars and engagements this person stood in, in the order they were recorded.
    /// </summary>
    /// <remarks>
    /// A soldier is remembered for the battles they reached, a ruler for the wars their realm
    /// fought, and anyone living in an invested town for the siege itself. Commanders were
    /// already named on the battle; this is how the rest of a life keeps the same facts.
    /// </remarks>
    public List<CampaignMemory> Campaigns { get; }

    /// <summary>Trips they made and came back from, in the order they were recorded.</summary>
    public List<Journey> Journeys { get; }

    /// <summary>Persistent, directed relationships to other people, in other-id order.</summary>
    public List<FigureBond> Bonds { get; }

    /// <summary>The bounded set of experiences this figure still carries.</summary>
    public List<SalientMemory> Memories { get; }

    /// <summary>Wounds got in battle or in a quarrel, with their recovery and any permanent result.</summary>
    public List<FigureInjury> Injuries { get; }

    /// <summary>Goals whose individual journeys, setbacks and attempts form a larger arc.</summary>
    public List<FigureUndertaking> Undertakings { get; }

    /// <summary>
    /// Personal quarrels this figure stands on one side of, in the order they were opened.
    /// </summary>
    /// <remarks>
    /// The entries are shared with the other party rather than copied, so neither page can come to
    /// believe a different version of the same quarrel.
    /// </remarks>
    public List<FigureDispute> Disputes { get; }

    /// <summary>
    /// Conspiracies this figure led or knowingly joined, in the order they began.
    /// </summary>
    /// <remarks>
    /// Shared with the other conspirators rather than copied, for the same reason a quarrel is.
    /// The target does not carry the plot against them, and neither does anybody whose access was
    /// used without their knowing it: this is the list of people who knew.
    /// </remarks>
    public List<FigurePlot> Plots { get; }

    /// <summary>What this figure wrote down about the sky, in the order they saw it.</summary>
    public List<SkyObservation> Observations { get; }

    /// <summary>What they said those sightings meant, and what the sky made of it.</summary>
    public List<SkyClaim> Claims { get; }

    public EntityId MotherId { get; set; } = EntityId.None;

    public EntityId FatherId { get; set; } = EntityId.None;

    /// <summary>Children, in birth order — appended as they are born.</summary>
    public List<EntityId> ChildIds { get; }

    /// <summary>Every marriage, in order. A widowed figure who remarries keeps both.</summary>
    public List<EntityId> SpouseIds { get; }

    /// <summary>The living spouse, if any. Cleared when a marriage ends in death.</summary>
    public EntityId SpouseId { get; set; } = EntityId.None;

    public bool IsMarried => !SpouseId.IsNone;

    /// <summary>Where this figure was born, if known.</summary>
    public EntityId BirthSettlementId { get; set; } = EntityId.None;

    /// <summary>
    /// What they were before the chronicle began following them.
    /// </summary>
    /// <remarks>
    /// <para>Set only for those raised into the record by an office. See
    /// <see cref="FigureOrigin"/> for why it is empty on everyone else.</para>
    ///
    /// <para><b>There is no birth event for these people, and there cannot be.</b> The chronicle is
    /// append-only in non-decreasing year order, so a birth forty years before the appointment that
    /// introduced them cannot be inserted — the same reason given in
    /// <see cref="World.Houses.NewFigure"/> for why a figure created grown gets no birth. What they
    /// do have is a real <see cref="BirthYear"/>, worked back from an age the office could
    /// plausibly be held at, so their page reads as a life rather than an entrance.</para>
    /// </remarks>
    public FigureOrigin Origin { get; set; } = FigureOrigin.Unrecorded;

    /// <summary>The year this figure was stripped of an office, if they ever were.</summary>
    /// <remarks>
    /// Kept on the figure rather than derived from the chronicle because the political-violence
    /// model asks the question every year for every court, and walking the event log to answer it
    /// would make a cheap check expensive. It is what makes <see cref="DeathCause.Execution"/>
    /// reachable by somebody who was not a losing claimant: before offices, the only way to be
    /// executed in this world was to lose a succession.
    /// </remarks>
    public int? DisgracedYear { get; set; }

    /// <summary>
    /// The year a spouse, parent, child or sibling was murdered, if one ever was.
    /// </summary>
    /// <remarks>
    /// The other half of political violence: a plot that stops at the one person it named is not
    /// how courts historically finished the job, and a widow or an heir who never appears in the
    /// aftermath is a chronicle that forgot they were related. Simulation-only, like
    /// <see cref="DisgracedYear"/> — the death event already carries the family on
    /// <c>extra</c>, which is what the viewer needs.
    /// </remarks>
    public int? KinMurderedYear { get; set; }

    /// <summary>The year this figure was named in a political murder, if they ever were.</summary>
    public int? AccusedYear { get; set; }

    /// <summary>
    /// Whoever they were named in the death of, or <see cref="EntityId.None"/>.
    /// </summary>
    public EntityId AccusedOfId { get; set; } = EntityId.None;

    /// <summary>
    /// Where this figure actually lives, or <see cref="EntityId.None"/> for "wherever the court is".
    /// </summary>
    /// <remarks>
    /// <para>Figures have a realm residence and, since offices, sometimes a finer one. That repairs
    /// a compromise recorded with the mortality model: a disaster could reach a figure only when it
    /// struck the capital, because the capital was the one settlement a court could honestly be
    /// placed in. A governor can honestly be placed in the town they govern, and so dies in its
    /// sack, its plague and its earthquake.</para>
    ///
    /// <para><b>An office never changes <see cref="CivilizationId"/>.</b> A posting moves someone
    /// within their realm, never between realms — see <c>HouseholdSystem.WhoMoves</c> for what
    /// happened the last time a figure was moved across a border carelessly.</para>
    /// </remarks>
    public EntityId ResidenceSettlementId { get; set; } = EntityId.None;

    public int AgeIn(int year) => year - BirthYear;

    public int? AgeAtDeath => DeathYear is null ? null : DeathYear.Value - BirthYear;

    /// <summary>Both parents, mother first, skipping any that are unrecorded.</summary>
    public IEnumerable<EntityId> Parents()
    {
        if (!MotherId.IsNone) yield return MotherId;
        if (!FatherId.IsNone) yield return FatherId;
    }

    /// <summary>The office currently held, if any. The most recently granted, where several are.</summary>
    public OfficeHolding? CurrentOffice
    {
        get
        {
            // Reverse order: the most recently granted office is the operative one.
            for (int i = Offices.Count - 1; i >= 0; i--)
            {
                if (Offices[i].ToYear is null) return Offices[i];
            }

            return null;
        }
    }

    /// <summary>The open holding of a given kind, if this figure has one.</summary>
    public OfficeHolding? OpenOffice(OfficeKind kind)
    {
        for (int i = Offices.Count - 1; i >= 0; i--)
        {
            if (Offices[i].ToYear is null && Offices[i].Kind == kind) return Offices[i];
        }

        return null;
    }

    public bool Holds(OfficeKind kind) => OpenOffice(kind) is not null;

    /// <summary>
    /// Closes the open office of a given kind as of <paramref name="year"/>.
    /// </summary>
    /// <remarks>
    /// Named by kind rather than by recency. The older <c>EndCurrentTitle</c> closed whichever
    /// open holding happened to be newest, which was unambiguous only while a figure could hold
    /// at most a crown and a regency; with marshals and governorships in the same list it would
    /// silently end the wrong one. Use <see cref="EndAllOffices"/> when the holder is what ended.
    /// </remarks>
    public void EndOffice(OfficeKind kind, int year)
    {
        for (int i = Offices.Count - 1; i >= 0; i--)
        {
            if (Offices[i].ToYear is null && Offices[i].Kind == kind)
            {
                Offices[i] = Offices[i] with { ToYear = year };
                return;
            }
        }
    }

    /// <summary>
    /// Closes every open office as of <paramref name="year"/>.
    /// </summary>
    /// <remarks>
    /// A figure can hold two at once — a regency for one realm and a throne of their own — and
    /// death ends both. Closing only the most recent left the older one open for ever, which
    /// surfaced as a regent still recorded as governing three centuries after they died. It took
    /// M8's plague to find: before it, the deaths that reached a double office-holder were rare
    /// enough not to occur in any tested seed.
    /// </remarks>
    public void EndAllOffices(int year)
    {
        for (int i = 0; i < Offices.Count; i++)
        {
            if (Offices[i].ToYear is null) Offices[i] = Offices[i] with { ToYear = year };
        }
    }

    /// <summary>Roman numerals, for regnal numbering.</summary>
    /// <remarks>
    /// Written out rather than table-indexed by a small cap, because "the fifteenth ruler of this
    /// name" is not a case anyone will think to test until a nine-hundred-year run produces one.
    /// </remarks>
    private static string Numeral(int value)
    {
        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        var text = new System.Text.StringBuilder(8);
        int remaining = value;

        for (int i = 0; i < values.Length && remaining > 0; i++)
        {
            while (remaining >= values[i])
            {
                text.Append(symbols[i]);
                remaining -= values[i];
            }
        }

        return text.ToString();
    }

    public override string ToString() =>
        $"{Id} {FullName} ({BirthYear}–{(DeathYear?.ToString() ?? string.Empty)})";
}
