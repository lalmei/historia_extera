using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// The ladder inside a trade, and what it takes to climb it.
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> A craft (#245) said which trade somebody practised and nothing
/// about their position in it: a guildsman was the same thing at sixteen as at sixty, which is the
/// emptiness <see cref="Ranks"/> was written to fix for soldiery. The guild ladder is also the one
/// part of working life that was <em>formally</em> recorded — an indenture, an admission, a name on
/// the work — so a craft without one was the odd career out both in this engine and in the
/// sources.</para>
///
/// <para><b>Time is necessary and never sufficient.</b> A term qualifies a man to be considered;
/// a company admits him, and how many it can admit is a fact about the town — see
/// <see cref="Establishment"/>. Below mastery the qualification is the term alone, because a boy
/// bound to a village smith was free of his indenture whether or not the town had a guild: the
/// issue's own rule that "where there is no guild, a craft has masters only in the loose sense"
/// cuts at mastery and not lower.</para>
///
/// <para><b>The company is the men of the trade in the town.</b> That is the documented rule the
/// acceptance asks for in place of a separate guild object, and it is not a new one — it is
/// exactly the rule <see cref="Guilds"/> already uses to decide that a mastery seat exists at all,
/// so "is there a company of weavers here" and "is there a weaver here" have one answer in both
/// files. What the town then decides is not whether a company exists but how many shops it has
/// room for, which is the honest shape: a village's two weaving households are a company with one
/// shop between them, and a city's hundreds are a company with dozens.</para>
///
/// <para><b>The seat now sits on top of the ladder instead of replacing it.</b> A mastery (#248)
/// is elected from the company's masters and from nobody else, which is who the vote actually was;
/// a town's master of the weavers who was not himself a master weaver would be a seat over a
/// ladder it had no place on. That is a real cost to #248 and it is the right one — measured, the
/// panel's masteries fall from 53–201 a world to 16–42 and its towns holding two or more from
/// 7–29 to 4–9, because a seat whose company has admitted nobody yet stands empty until it has.
/// The alternative was tried first and is worth recording:
/// letting the election confer the mastery made 60–69% of every world's craftsmen masters, since
/// the number of seats turning over in three centuries is of the same order as the number of
/// craftsmen the chronicle names at all, so nearly every one of them was elected to something.
/// An election cannot be the ordinary way into a grade that is supposed to be exceptional.</para>
///
/// <para><b>Two things read it</b>, which is the standard this engine holds a new dimension to.
/// <see cref="Makers"/> names a master on an object and nobody else, so the work of a company that
/// has admitted nobody goes out anonymous or to a man the levy finds — which is both what being a
/// master meant and a way of keeping famous objects scarce. Scarcer is not what happened, and the
/// reason is the levy: a town whose recorded potters are all journeymen is a town whose masters
/// were never written down, which is the case <see cref="Levies"/> exists for, so across the panel
/// made objects naming a maker went from 33 to 42 — 0–14 a world against 1–11 — and every one of
/// them is now a master rather than whichever practitioner the town happened to hold. And
/// <see cref="Guilds"/> elects a mastery seat only from masters, so the ladder decides who speaks
/// for a trade.</para>
///
/// <para><b>And the middle rung is the one allowed to leave.</b> The word is the mechanism: a man
/// free of his indenture with no shop of his own travelled to find one, and
/// <see cref="Entities.JourneyKind.Wandering"/> is that road. It is the only journey in the engine
/// whose purpose is that it may not end at the traveller's own hearth, so it settles its traveller
/// at about ten times a merchant's rate; measured across the panel, 625 wanderings and 52 new
/// homes. A guildsman whose own town could support no trade at all takes one up where he settles,
/// which is what makes the road worth walking for somebody who never got an indenture — and it is
/// the mechanism #251 asks for when a craft has to reach a town that does not have it, rather than
/// a second one invented beside it.</para>
///
/// <para>Kept out of the yearly system for the reason <see cref="Ranks"/> is: two paths reach a
/// grade — a term served, and a levy that finds a man already at the work — and both must assemble
/// the same row or one of them is a stage the viewer cannot render.</para>
/// </remarks>
public static class Grades
{
    /// <summary>The rung a company's own master stands on, and the top of every ladder.</summary>
    public const CraftGrade Top = CraftGrade.Master;

    /// <summary>The claim recorded on a mastery the record found rather than watched happen.</summary>
    /// <remarks>
    /// The documented exception the acceptance asks for, and the object is what documents it. A
    /// levy (<see cref="Levies.ForMaking"/>) is raised because something was made and the town's
    /// craftsmen were never written down; the man it finds is already at the work and already of
    /// the age to keep a shop — see <see cref="Levies.TradeAge"/>, which puts him at 28 and over.
    /// Binding him as an apprentice would make the object older than its maker's trade.
    /// </remarks>
    public const string FoundClaim = "on being found at the work";

    /// <summary>
    /// Years at this rung before the next is considered.
    /// </summary>
    /// <remarks>
    /// <para>The terms the indentures actually used. Seven years bound is the classic English
    /// apprenticeship and close to the continental norm; the wander-years after it were shorter and
    /// less uniform, and six is the middle of what the ordinances asked for.</para>
    ///
    /// <para>They also have to fit a life. A craft is taken at <see cref="Succession.MajorityAge"/>,
    /// so the earliest possible mastery is twenty-nine — a man with a shop of his own in his
    /// thirties, which is what the age bands <see cref="Levies.TradeAge"/> already assumed for a
    /// craftsman the record finds keeping one.</para>
    /// </remarks>
    public static int Term(CraftGrade grade) => grade switch
    {
        CraftGrade.Apprentice => 7,
        CraftGrade.Journeyman => 6,
        _ => 0,
    };

    /// <summary>The rung above this one, or the same rung at the top of the ladder.</summary>
    public static CraftGrade Next(CraftGrade grade) => grade switch
    {
        CraftGrade.None => CraftGrade.Apprentice,
        CraftGrade.Apprentice => CraftGrade.Journeyman,
        _ => CraftGrade.Master,
    };

    /// <summary>Yearly chance a man who has served his term is made free of the trade.</summary>
    /// <remarks>
    /// High, because finishing an indenture was close to a formality — the term <em>is</em> the
    /// qualification, and what varies is which year the company got round to it. Not one, for the
    /// reason no seat is filled the year it falls vacant.
    /// </remarks>
    private const double FreedomChance = 0.45;

    /// <summary>Yearly chance a journeyman with a place open to him is admitted master.</summary>
    /// <remarks>
    /// <para><b>This is where the aggregate is projected onto the record, and it has to be a
    /// probability rather than a cap.</b> A recorded craftsman is one draw from a company that is
    /// almost entirely unrecorded — see <see cref="Crafts.Practitioners"/> — and about one man in
    /// three of that company keeps a shop. A cap over the recorded members cannot express that: a
    /// town with two weavers written down would have to allow either none or one, which is either
    /// no masters anywhere or half the company in a shop.</para>
    ///
    /// <para><b>Measured against a career, not a year.</b> A journeyman is due after his six years
    /// and lives perhaps twenty-five more, so the yearly figure that leaves a third of them
    /// admitted is around a fiftieth. Measured across the panel below; the establishment still
    /// binds it in small places, where the town's whole trade has room for one shop.</para>
    /// </remarks>
    private const double AdmissionChance = 0.016;

    /// <summary>Members of a company per master it has room for.</summary>
    /// <remarks>
    /// <para>A shop, not a rank. A master kept a shop with journeymen and apprentices working in
    /// it, so how many masters a trade can carry is a fact about the size of the trade — which is
    /// the same argument <see cref="Ranks.Establishment"/> makes about an army being a shape rather
    /// than a queue, and it is needed here for the same reason: without it every craftsman who
    /// lived long enough would end a master and the ladder would read as a schedule.</para>
    ///
    /// <para>Three, so a company of three has one master and a company of nine has three. Deeper
    /// would leave the ordinary trades — the ones a town has two or three of — permanently unable
    /// to admit a second man, and mastery would become a fact about which craftsman was born
    /// first.</para>
    /// </remarks>
    private const int MembersPerMaster = 3;

    /// <summary>What a single word calls this rung.</summary>
    public static string Label(CraftGrade grade) => grade switch
    {
        CraftGrade.Apprentice => "apprentice",
        CraftGrade.Journeyman => "journeyman",
        CraftGrade.Master => "master",
        _ => "guildsman",
    };

    /// <summary>
    /// What the chronicle calls a man of this grade in this trade: "a master mason".
    /// </summary>
    /// <remarks>
    /// The two halves in the order the record put them, and the whole reason the grade is worth
    /// carrying: "a mason" is what the engine could say before, and "the master mason of Aldenmoor"
    /// is what a chronicler wrote.
    /// </remarks>
    public static string Phrase(CraftGrade grade, Craft craft) =>
        grade == CraftGrade.None
            ? Crafts.Label(craft)
            : Label(grade) + " " + Crafts.Label(craft);

    /// <summary>How the record words a company's admission of one of its own.</summary>
    public static string AdmissionClaim(Craft craft, string town) =>
        "by the admission of the " + Crafts.Company(craft) + " of " + town;

    /// <summary>Whether this figure is somebody a trade would have a grade for.</summary>
    /// <remarks>
    /// A craft and a life. Not gated on <see cref="Occupation.Guild"/>: a mason who took a
    /// governorship is a mason still — <see cref="Crafts"/> is explicit that a trade is not
    /// unlearned — and the years he spends at court are years his company goes on counting him.
    /// </remarks>
    public static bool Practises(Figure figure, int year) =>
        figure.IsAlive
        && figure.Craft != Craft.None
        && figure.AgeIn(year) >= Succession.MajorityAge;

    /// <summary>
    /// How many shops this trade has room for in a town of this size.
    /// </summary>
    /// <remarks>
    /// <para><b>One master to every three of a trade's men — and a trade has more men than the
    /// chronicle names.</b> The company is in the population number, not in the figure table
    /// (<see cref="Crafts.Practitioners"/>), so this is asked of the town. Reading the recorded
    /// members instead would make a city's loom trade the size of a hamlet's, because both have
    /// two or three weavers written down.</para>
    ///
    /// <para><b>It binds in small places and nowhere else</b>, which is the division of labour
    /// between it and <see cref="AdmissionChance"/>. A village whose whole loom trade is two
    /// households has room for one shop, and the second recorded weaver there stays a journeyman
    /// however long he lives; a city has room for more shops than the chronicle names weavers, so
    /// what decides a city's masters is the yearly chance alone.</para>
    ///
    /// <para>At least one wherever the trade exists at all, so a village with a single smith is not
    /// a village that can never have a master smith.</para>
    ///
    /// <para>It governs admission, not arrival. A levy (<see cref="FoundClaim"/>) can carry a
    /// company past it, being a fact about the record rather than a promotion the company decided
    /// on. What this guarantees is that nobody is <em>admitted</em> into a place the trade does not
    /// have.</para>
    /// </remarks>
    public static int Establishment(int inTown) => Math.Max(1, inTown / MembersPerMaster);


    /// <summary>
    /// Whether this figure is due for the next rung, and how likely they are to reach it this year.
    /// </summary>
    /// <remarks>
    /// Zero where they are not due at all, so a caller can treat the whole judgement as one number.
    /// The gate mastery adds on top of this is <see cref="Admits"/> and
    /// <see cref="Establishment"/>, which are facts about the town rather than about the man and
    /// so are asked by the pass that holds the census.
    /// </remarks>
    public static double AdvanceOdds(Figure figure, int year)
    {
        CraftStep? step = figure.CurrentGrade;
        if (step is null) return 0.0;

        CraftGrade next = Next(step.Grade);
        if (next == step.Grade) return 0.0;
        if (year - step.Year < Term(step.Grade)) return 0.0;

        return next == CraftGrade.Master ? AdmissionChance : FreedomChance;
    }

    /// <summary>
    /// Puts somebody on a rung and records it.
    /// </summary>
    /// <remarks>
    /// The single assembly point all three paths go through, for the reason
    /// <see cref="Ranks.Raise"/> is one. Refuses a rung already reached, so nobody is admitted
    /// twice, and refuses the dead, so nobody advances after they are gone.
    /// </remarks>
    public static void Raise(
        WorldState world, Figure figure, CraftGrade grade, string? claim, int year)
    {
        if (grade == CraftGrade.None || figure.Craft == Craft.None) return;
        if (!figure.IsAlive || figure.Grade >= grade) return;

        EntityId town = world.ResidenceOf(figure);
        figure.Grades.Add(new CraftStep(grade, town, year) { Claim = claim });

        // The apprenticeship is not a second event. Crafts.Take has already written
        // CraftTaken for the same year — "was set to the trade of smith" is the indenture — and a
        // line saying the same thing twice is the duplication the office model already refuses.
        if (grade == CraftGrade.Apprentice) return;

        string phrase = Phrase(grade, figure.Craft);
        DetMap<string, string> data = claim is null
            ? Chronicle.Data(("grade", phrase))
            : Chronicle.Data(("grade", phrase), ("claim", claim));

        // A freedom is the parish register of a trade: everybody who serves a term reaches it. An
        // admission is a decision a company made about a person, and stays.
        world.Chronicle.Record(
            year,
            EventKind.CraftAdvanced,
            figure.Id,
            location: town,
            data: data,
            significance: grade >= Top ? Significance.Notable : Significance.Routine);
    }

    /// <summary>
    /// Puts somebody who has just taken a craft at the standing they enter it with.
    /// </summary>
    /// <remarks>
    /// Silent for anyone who already has one, so the call is safe to make wherever a craft is
    /// set. Almost always the indenture; <see cref="FoundClaim"/> is the exception and is argued
    /// on the constant.
    /// </remarks>
    public static void Enter(
        WorldState world, Figure figure, CraftGrade grade, string? claim, int year)
    {
        if (figure.Grade != CraftGrade.None) return;

        Raise(world, figure, grade, claim, year);
    }

    /// <summary>Binds somebody who has just taken a craft and has no standing in it yet.</summary>
    public static void Bind(WorldState world, Figure figure, int year) =>
        Enter(world, figure, CraftGrade.Apprentice, null, year);

    /// <summary>
    /// Advances everyone a year has made due: binds, frees, and admits what the companies allow.
    /// </summary>
    /// <remarks>
    /// <para><b>One pass over the figures, then one over the towns</b>, for the reason
    /// <see cref="Guilds.Fill"/> is built that way: asking each town who lives in it is the figure
    /// table read once per settlement per year, which grows with the number of towns as well as
    /// with the number of people. Bucketing once costs the same read whichever shape the world
    /// is.</para>
    ///
    /// <para><b>A freedom is decided in the figure pass and an admission in the town pass</b>,
    /// because they answer to different things. Serving out an indenture is between a man and his
    /// term; being admitted master is between him, his trade's open shops and the men standing
    /// beside him — so it cannot be settled until the whole company has been counted.</para>
    ///
    /// <para><b>The senior journeyman is offered the shop first.</b> Standing in a guild was time
    /// served, which is the same rule <see cref="Guilds"/> weighs a vote by; ties break on id,
    /// which is append-ordered. Each man still makes his own yearly roll, so a trade does not admit
    /// its whole bench the year a shop falls empty, and one who fails it leaves the place for the
    /// next man rather than holding it.</para>
    ///
    /// <para>Forked per town and per trade, so a town's weavers cannot reshuffle its smiths, and
    /// then per figure, so a craftsman coming of age cannot shift the admissions of everyone
    /// already in the trade.</para>
    /// </remarks>
    public static void Advance(WorldState world, int year, IRng rng)
    {
        var towns = new Dictionary<EntityId, Bench[]>();

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (figure.AgeIn(year) < Succession.MajorityAge) continue;

            // The trade a man had no chance to take at home. Crafts.Ensure reads the town he lives
            // in now, so a guildsman who left a place that could support nothing and settled where
            // a trade is practicable takes it up there. Silent and idempotent for everyone else:
            // it is forked on the figure's own id, so asking again in a later year cannot draw a
            // different trade, only a trade where there was none.
            if (figure.Occupation == Occupation.Guild && figure.Craft == Craft.None)
            {
                Crafts.Ensure(world, figure, year);
            }

            if (!Practises(figure, year)) continue;

            // The safety net, not the ordinary path: Crafts.Take binds in the same call that sets
            // the trade. This catches a craft set by any future writer that forgets to.
            Bind(world, figure, year);

            EntityId home = world.ResidenceOf(figure);
            if (home.IsNone) continue;

            if (!towns.TryGetValue(home, out Bench[]? benches))
            {
                towns[home] = benches = new Bench[Crafts.All.Length];
            }

            int slot = Slot(figure.Craft);
            benches[slot] ??= new Bench();
            Bench bench = benches[slot]!;

            bench.Members++;
            if (figure.Grade >= Top) bench.Masters++;

            if (Next(figure.Grade) == CraftGrade.Journeyman)
            {
                Free(world, figure, year, rng);
                continue;
            }

            if (Next(figure.Grade) != Top) continue;
            if (AdvanceOdds(figure, year) <= 0.0) continue;

            bench.Due.Add(figure);
        }

        // World order, which is append-ordered, rather than the dictionary's.
        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive) continue;
            if (!towns.TryGetValue(settlement.Id, out Bench[]? benches)) continue;

            IRng local = rng.Fork("town", settlement.Id.ToDiscriminator());

            // Crafts.All order, the one declared order every walk over trades uses.
            for (int slot = 0; slot < Crafts.All.Length; slot++)
            {
                Bench? bench = benches[slot];
                if (bench is null || bench.Due.Count == 0) continue;

                Craft craft = Crafts.All[slot];

                int places =
                    Establishment(Crafts.Practitioners(world, settlement, craft)) - bench.Masters;
                if (places <= 0) continue;

                Senior(bench.Due);

                IRng company = local.Fork("craft", (long)craft);
                string claim = AdmissionClaim(craft, settlement.Name);

                foreach (Figure journeyman in bench.Due)
                {
                    if (places <= 0) break;

                    if (!company.Fork("admission", journeyman.Id.ToDiscriminator())
                        .Chance(AdvanceOdds(journeyman, year)))
                    {
                        continue;
                    }

                    Raise(world, journeyman, Top, claim, year);
                    places--;
                }
            }
        }
    }

    /// <summary>The company of one trade in one town, as a year's pass counts it.</summary>
    /// <remarks>
    /// The recorded half only. <see cref="Crafts.Practitioners"/> is the other half and is asked of
    /// the town, because most of a trade is inside the population number rather than in this list.
    /// </remarks>
    private sealed class Bench
    {
        public int Members;

        public int Masters;

        public List<Figure> Due { get; } = new();
    }

    /// <summary>This craft's place in <see cref="Crafts.All"/>.</summary>
    private static int Slot(Craft craft) => (int)craft - 1;

    /// <summary>Longest in the trade first, then by id, which is a total order.</summary>
    private static void Senior(List<Figure> due) => due.Sort((left, right) =>
    {
        int byYear = left.CraftYear.CompareTo(right.CraftYear);
        return byYear != 0 ? byYear : left.Id.CompareTo(right.Id);
    });

    /// <summary>Makes a man free of his trade once his term is served.</summary>
    /// <remarks>
    /// No town gate, and that is the issue's own rule: where there is no guild a craft has masters
    /// "only in the loose sense", which cuts at mastery and not below it. A boy bound to a village
    /// smith was out of his indenture whether or not the place had a company to say so.
    /// </remarks>
    private static void Free(WorldState world, Figure figure, int year, IRng rng)
    {
        double odds = AdvanceOdds(figure, year);
        if (odds <= 0.0) return;

        if (!rng.Fork("freedom", figure.Id.ToDiscriminator()).Chance(odds)) return;

        Raise(world, figure, CraftGrade.Journeyman, null, year);
    }
}
