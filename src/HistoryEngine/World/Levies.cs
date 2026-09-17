using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// Raises the craftsman a year needs out of the population a town already holds.
/// </summary>
/// <remarks>
/// <para><b>Recorded figures are a thin sample of the people in a world.</b> A town of nine
/// thousand holds a handful of them, and none is guaranteed to be the one the year needs. So the
/// moment an object wants a named maker, the common case is that the trade exists in the town and
/// nobody in the record holds it — the smiths are all inside the population number. Across the
/// five-seed panel at three hundred years, 4 of 49 objects could name a maker; the other 45 were
/// made in towns whose craftsmen were never written down.</para>
///
/// <para><b>This is the same door <see cref="Offices"/> already opens, for the same reason.</b>
/// <see cref="Offices.IsAppointed"/> marks the seats somebody can be raised out of the population
/// into, <see cref="Offices.Notable"/> works a birth year back from a plausible career length, and
/// a marshal who did not exist last year exists now because the realm needed one. A craft is the
/// same shape of fact: <see cref="FigureOrigin.Guild"/> has been declared for precisely this door
/// since crafts landed, and until now nothing walked through it.</para>
///
/// <para><b>Demand pulls, and nothing else does.</b> A craftsman is raised because something is
/// being made this year and wants a maker. There is no background trickle of guildsmen: if nothing
/// needs a cooper, no cooper is raised, and a world that makes nothing raises nobody. That is what
/// keeps this a sampling door out of the unrecorded population rather than a second birth rate.</para>
///
/// <para><b>The levy may not become a back door around the gate.</b> <see cref="Crafts.Supports"/>
/// is the one place that says what a trade needs from the ground, the water and the road, and it
/// is asked here exactly as it is asked of someone coming of age. A goldsmith is not raised where
/// there is no wealth, a shipwright not where there is no sheltered water, and neither below the
/// tier their trade needs a market at. A raised craftsman is a person the record had missed, not a
/// person the place could not have held.</para>
///
/// <para><b>They cost the attention budget, so the budget is respected by construction.</b>
/// <see cref="Offices.HeadsAHousehold"/> deliberately restricts which raised figures pull a
/// household into the record, because promoting them is "a growth rate rather than a level shift".
/// A levied craftsman holds no appointed office, so that predicate already answers no for them:
/// they are named, dated and credited with their work, and the chronicle does not follow them into
/// a nursery. A levy that founded a household for every barrel made would be exactly the failure
/// the attention budget exists to refuse.</para>
/// </remarks>
public static class Levies
{
    /// <summary>
    /// How many inhabitants a town needs per craftsman it is allowed to have missed.
    /// </summary>
    /// <remarks>
    /// <para><b>The ceiling, and the reason the levy cannot compound.</b> A town may hold one
    /// levied craftsman per this many people, counting the ones still alive. At 900 — the
    /// population at which a settlement becomes a <see cref="SettlementTier.Town"/> — a town
    /// supports one, and a city of four thousand supports four. The rule is deliberately about the
    /// living rather than the raised: a levy answers "was there a smith in this town", and when
    /// that smith dies the question is open again, which is the honest shape of the thing.</para>
    ///
    /// <para>Two independent bounds sit above this one and both matter. A levy fires only where
    /// <see cref="Makers.Find"/> found nobody, so a town that already holds a smith never raises a
    /// second; and objects are capped per town by the treasury limit, so demand itself is finite.
    /// This number is the floor under those, and it is what stops a large, productive city from
    /// turning a century of commissions into a census.</para>
    /// </remarks>
    public const int PeoplePerLevy = 900;

    /// <summary>
    /// The age at which a craftsman raised from the ordinary population is first recorded.
    /// </summary>
    /// <remarks>
    /// <para>Career length, not a uniform guess — the argument <see cref="Offices"/> makes for
    /// <c>CareerAge</c>, applied to a trade instead of a seat. A craftsman enters the record at the
    /// moment his work was worth writing down, and the band is how long it took to be able to do
    /// that work. A master goldsmith has served an apprenticeship and is not twenty-two.</para>
    ///
    /// <para><b>The long trades.</b> Goldsmith, glassblower, armourer and shipwright are the ones
    /// whose training was genuinely long and whose materials were too expensive to learn on: the
    /// band starts in the thirties because nobody was trusted with gold, glass, plate or a hull
    /// before then. The bookbinder joins them for a different reason — the trade needs a town that
    /// reads, so a binder worth naming is one who has already outlived a patron or two.</para>
    ///
    /// <para><b>The trades of a town.</b> Smith, mason, miller, dyer, brewer and wheelwright open
    /// in the late twenties: a shop of one's own, which is what being named requires, came after
    /// journeyman years but not after a lifetime.</para>
    ///
    /// <para><b>The broad trades.</b> Weaver, potter, baker, tanner, cooper, carpenter and
    /// charcoal burner open in the mid-twenties. These were entered early and practised by many,
    /// and a competent one at twenty-five is not implausible the way a master goldsmith at
    /// twenty-five is.</para>
    ///
    /// <para><b>The sailor is the exception in both directions.</b> Entered younger than any other
    /// trade here — the sea took boys — and closing earlier, because it did not keep old men. He
    /// is the one craft on this table where a man of twenty-two is the ordinary case rather than
    /// the surprising one.</para>
    ///
    /// <para>The upper bound is not a life expectancy. It is the oldest a person could be and still
    /// be starting the stretch of work the record is about to credit them with; mortality takes it
    /// from there, on the same table as everybody else.</para>
    /// </remarks>
    public static (int Min, int Max) TradeAge(Craft craft) => craft switch
    {
        // Long apprenticeships on materials too dear to practise on.
        Craft.Goldsmith => (34, 58),
        Craft.Glassblower => (33, 56),
        Craft.Armourer => (32, 55),
        Craft.Shipwright => (34, 58),
        Craft.Bookbinder => (31, 54),

        // A shop of one's own, in a town.
        Craft.Smith => (28, 52),
        Craft.Mason => (29, 54),
        Craft.Miller => (28, 52),
        Craft.Dyer => (28, 51),
        Craft.Brewer => (28, 51),
        Craft.Wheelwright => (28, 50),
        Craft.Apothecary => (30, 55),
        Craft.Salter => (27, 51),

        // Entered early, practised by many.
        Craft.Weaver => (24, 48),
        Craft.Potter => (24, 48),
        Craft.Baker => (25, 48),
        Craft.Tanner => (25, 48),
        Craft.Cooper => (25, 48),
        Craft.Carpenter => (26, 50),
        Craft.CharcoalBurner => (23, 46),

        // The sea took boys and did not keep old men.
        Craft.Sailor => (20, 42),

        _ => (26, 48),
    };

    /// <summary>
    /// Raises a craftsman of a named trade in a named settlement, or returns none.
    /// </summary>
    /// <remarks>
    /// <para><b>The single entry point.</b> Everything that wants a craftsman the record does not
    /// hold comes through here, so the gate, the ceiling and the age band are asked once and in one
    /// place. It answers <see langword="null"/> whenever the town may not hold the trade, and a
    /// caller that gets <see langword="null"/> should record what it was going to record anonymously
    /// — anonymity stays a result rather than a failure, exactly as <see cref="Makers"/> has it.</para>
    ///
    /// <para><b>Forked on the settlement, the year and the trade</b>, so what a levy produces cannot
    /// depend on how many towns were visited before it or on what was raised elsewhere in the same
    /// year. Raising a smith in one town cannot shift the age of a weaver in another.</para>
    /// </remarks>
    public static Figure? Raise(WorldState world, Settlement settlement, Craft craft, int year)
    {
        if (craft == Craft.None) return null;
        if (!world.Civilizations.Contains(settlement.CivilizationId)) return null;

        // The gate, unchanged and unbypassed: the ground, the water, the road and the tier floor.
        if (!Crafts.Supports(world, settlement, craft)) return null;

        // The ceiling. Counted over the living, because a levy answers a question that reopens
        // when the man it answered for dies.
        if (!HasRoomFor(world, settlement)) return null;

        Civilization civilization = world.Civilizations[settlement.CivilizationId];
        Culture culture = world.CultureOf(civilization);

        IRng rng = world.Root
            .Fork("levy", settlement.Id.ToDiscriminator())
            .Fork("year", year)
            .Fork("craft", (long)craft);

        (int min, int max) = TradeAge(craft);

        // Even, like every office but a cleric's. Which trades admitted whom is a question about
        // guilds rather than about people, and this file does not model guilds.
        Sex sex = rng.Chance(0.5) ? Sex.Male : Sex.Female;

        Figure craftsman = Houses.NewFigure(
            world,
            civilization,
            culture,
            sex,
            year - rng.NextInt(min, max + 1),
            birthSettlementId: settlement.Id);

        craftsman.Origin = FigureOrigin.Guild;
        craftsman.Background = new FigureBackground(
            year, settlement.Id, Upbringings.FamilyOf(Occupation.Guild))
        {
            // The body the career was made in, which every raised background is required to name.
            // A guild was a town body and guilds have no identity of their own yet, so the town is
            // not a placeholder here — it is the truest thing the model can currently say. When
            // #248 gives a town its several guilds, this becomes the guild.
            InstitutionId = settlement.Id,
        };

        // Both records, in the order a guildsman coming of age leaves them: entered a guild, and
        // the guild was the coopers'. Written through Occupations rather than by assignment,
        // because a trade with no office and no OccupationTaken means something else entirely in
        // this engine — somebody who died before a career was ever chosen for them.
        Occupations.EnterCareer(world, craftsman, Occupation.Guild, year);
        Crafts.Take(world, craftsman, craft, year);

        return craftsman;
    }

    /// <summary>
    /// The craftsman who made this kind of thing in this town, raising one if the trade was there
    /// to be had and nobody in the record held it.
    /// </summary>
    /// <remarks>
    /// <para><b>Find first, always.</b> A town that holds a goldsmith uses him; the levy exists for
    /// the town that holds none. Reversing the two would invent a rival for a man already in the
    /// record, which is the one outcome that would make the record worse rather than fuller.</para>
    ///
    /// <para><b>Best claim first, and only the claims the place can support.</b>
    /// <see cref="Makers.CraftsFor"/> ranks the trades that could have produced a kind — an
    /// armourer before a smith for a blade — and the levy walks that order, raising the first the
    /// town could have held. A town with metal and no wealth raises a smith for a blade and nobody
    /// at all for a crown.</para>
    ///
    /// <para><b>The map is updated, so a year cannot raise twice.</b> A town that commissions two
    /// jewels in one year gets one goldsmith, because the first levy is written into the same
    /// lookup the second one reads.</para>
    /// </remarks>
    public static EntityId ForMaking(
        WorldState world,
        Settlement settlement,
        ArtifactKind kind,
        int year,
        Dictionary<(EntityId Town, Craft Trade), Figure> guildsmen)
    {
        EntityId found = Makers.Find(settlement.Id, kind, guildsmen);
        if (!found.IsNone) return found;

        foreach (Craft craft in Makers.CraftsFor(kind))
        {
            Figure? raised = Raise(world, settlement, craft, year);
            if (raised is null) continue;

            guildsmen[(settlement.Id, craft)] = raised;
            return raised.Id;
        }

        return EntityId.None;
    }

    /// <summary>
    /// Whether the town is large enough to have missed another craftsman.
    /// </summary>
    /// <remarks>
    /// One pass over the figures of the settlement rather than a cached count: a levy is rare —
    /// a handful a century across a world — and a count that had to be maintained would be a
    /// second source of truth about who is alive.
    /// </remarks>
    private static bool HasRoomFor(WorldState world, Settlement settlement)
    {
        int allowance = settlement.Population / PeoplePerLevy;
        if (allowance <= 0) return false;

        int standing = 0;

        foreach (Figure figure in world.Figures)
        {
            if (figure.Origin != FigureOrigin.Guild) continue;
            if (!figure.IsAlive) continue;
            if (figure.ResidenceSettlementId != settlement.Id) continue;

            standing++;
            if (standing >= allowance) return false;
        }

        return true;
    }
}
