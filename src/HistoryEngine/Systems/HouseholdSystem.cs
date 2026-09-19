using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Marries the houses off and fills their nurseries.
/// </summary>
/// <remarks>
/// <para><b>The problem this system exists to solve is not fertility, it is attention.</b> A house
/// where everyone marries and every couple has children grows exponentially: three children a
/// generation over ten generations is fifty thousand people per realm, and a history generator that
/// runs in milliseconds stops doing so. Capping births would bound the count and produce a
/// chronicle of implausibly small families.</para>
///
/// <para>What is capped instead is <em>proximity to the throne</em>. A figure is married off, and a
/// couple has children, only while they stand near the front of their house's line — see
/// <see cref="Succession.Kin"/>. As the ruler's own children are born, cousins are pushed down the
/// line and quietly stop being written about. That is not a claim about who had children; it is a
/// claim about whose children a chronicle bothers to name, which is exactly the right thing for
/// this engine to be modelling, and it makes the figure table grow linearly in the number of
/// reigns rather than exponentially in the number of generations.</para>
///
/// <para>Runs after <see cref="SuccessionSystem"/>, so the year's marriages and births are measured
/// against the line as it stands after any coronation — a new king's brothers are demoted the same
/// year he is crowned, not the year after.</para>
/// </remarks>
public sealed class HouseholdSystem : ISystem
{
    /// <summary>
    /// Youngest age at which a figure may be married.
    /// </summary>
    /// <remarks>
    /// Internal rather than private since Milestone 32: <see cref="Affinities"/> reads it too, so
    /// that the age a courtship may climb to <see cref="AffinityStage.Lover"/> at is the same age
    /// a marriage would accept, rather than a second number somebody has to remember to keep in
    /// step with this one.
    /// </remarks>
    internal const int MarriageAge = 16;

    /// <summary>Rank in the line beyond which the chronicle stops arranging marriages.</summary>
    private const int MarriageableRank = 8;

    /// <summary>
    /// Rank beyond which a couple's children go unrecorded.
    /// </summary>
    /// <remarks>
    /// Six positions rather than the four an eye for tidiness suggests, and the difference is not
    /// cosmetic. Most of a line at any moment is children and the elderly, so four positions leave
    /// a house with barely one couple of childbearing age — and one couple against a fifth infant
    /// mortality is a house that fails within three generations. At six, a world keeps a spread of
    /// houses alive instead of converging on the two that got lucky early.
    /// </remarks>
    private const int FertileRank = 5;

    /// <summary>
    /// Ranks added to every member of a house that holds no throne.
    /// </summary>
    /// <remarks>
    /// Chosen against the two gates above so that a house out of power is followed at its head and
    /// nowhere else: one couple still has children, three more members are still married off, and
    /// the rest of the family passes out of the record. That is enough for a house to survive a
    /// century in opposition and be elected back — and little enough that a world's dormant houses
    /// do not between them outnumber its reigning ones.
    /// </remarks>
    private const int DormantHouseRank = 5;

    /// <summary>
    /// Rank given to the head of a household an office raised.
    /// </summary>
    /// <remarks>
    /// <para>Zero, because they are the head of their own household and there is nothing above them
    /// to be ranked behind. The map is a distance from whoever a line is followed <em>from</em>,
    /// which for a dynasty is its throne and for a raised notable is themself.</para>
    ///
    /// <para><b>Only the head is ranked, and that is the entire bound.</b> Their spouse needs no
    /// rank — <see cref="Bear"/> asks whether either parent is near enough — and their children get
    /// none, so a notable's children are recorded, grow up, and are not themselves extended. A
    /// child who takes an office becomes the head of a household in their own right and is ranked
    /// here for it, which is the one door out of that and the one the design named.</para>
    /// </remarks>
    private const int NotableHeadRank = 0;

    /// <summary>Yearly chance an eligible unmarried figure is matched.</summary>
    private const double MarriageChance = 0.34;

    /// <summary>Base chance a fertile marriage produces a child in a given year.</summary>
    private const double BirthChance = 0.26;

    /// <summary>Chance a birth kills the mother.</summary>
    private const double ChildbedRisk = 0.022;

    /// <summary>Age past which a woman bears no further recorded children.</summary>
    private const int LastChildbearingAge = 44;

    /// <summary>Base odds a match is sought among the other houses rather than at home.</summary>
    private const double ForeignMatchFloor = 0.25;

    /// <summary>Extra odds of a foreign match at full <see cref="CultureValues.Mercantile"/>.</summary>
    private const double ForeignMatchFromTrade = 0.5;

    /// <summary>
    /// Odds a figure with a standing lover marries them rather than entering the political draw.
    /// </summary>
    /// <remarks>
    /// High rather than certain. A dynasty's marriages remain arranged first and felt second — that
    /// is what moves houses between realms and is not a mistake this milestone corrects — so a
    /// standing courtship weighs the roll heavily toward itself without removing the chance that a
    /// house's needs simply come first this year, which is the case <see cref="Affinities"/> then
    /// reads as <see cref="AffinityOutcome.Overridden"/>.
    /// </remarks>
    private const double CourtshipMarriageChance = 0.70;

    public string Name => "houses";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        IRng rng = world.Root.Fork(Name, year);
        DetMap<EntityId, int> ranks = RankEveryHouse(world, year);

        ComeOfAge(world, year);
        Marry(world, ranks, year, rng);
        WedCourtships(world, ranks, year);
        Bear(world, ranks, year, rng);
    }

    /// <summary>
    /// Every living dynast's distance from the nearest throne their house has a claim on.
    /// </summary>
    /// <remarks>
    /// <para>A <see cref="DetMap{TKey,TValue}"/> rather than a dictionary: this is built once a year
    /// and read for every figure, and a dictionary's iteration order would leak into which of two
    /// equally-ranked cousins got married first — see <c>DeterminismGuardTests</c>.</para>
    ///
    /// <para><b>The budget is per court, not per family.</b> A house is ranked once for every
    /// throne it holds, so the number of people a world follows tracks the number of realms in it
    /// rather than the number of surviving houses. Ranking per house instead is the tidier reading
    /// and quietly starves a long run: houses consolidate, and an eight-century world ends with
    /// two families and fifty living people in it. What made per-court ranking a runaway — a
    /// larger house winning more elections and so breeding faster still — is dealt with where it
    /// happens, in <see cref="Succession.Claimants"/>, which passes over a house that already
    /// rules elsewhere.</para>
    ///
    /// <para>Reigning houses are ranked first and dormant ones second, taking the lower of the two
    /// wherever they overlap. A house that rules somewhere is therefore never penalised for also
    /// being out of power somewhere else, and the second pass needs no test for which houses the
    /// first one already covered.</para>
    ///
    /// <para><b>Households an office raised are ranked in the same map</b>, and that is what keeps
    /// the two attention budgets from contradicting each other. They are one budget: a person is
    /// followed, or is not, and the same lookup answers it for a king's fourth son and for a
    /// governor raised out of a provincial town.</para>
    /// </remarks>
    private static DetMap<EntityId, int> RankEveryHouse(WorldState world, int year)
    {
        var ranks = new DetMap<EntityId, int>();

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Rank(ranks, Succession.Kin(world, civilization), 0);
        }

        foreach (Dynasty house in world.Dynasties)
        {
            if (house.IsExtinct) continue;

            Rank(ranks, Succession.Kin(world, house), DormantHouseRank);
        }

        RankNotables(world, ranks, year);

        return ranks;
    }

    /// <summary>
    /// The heads of the households the offices raised.
    /// </summary>
    /// <remarks>
    /// Assigned rather than merged, because <see cref="Offices.HeadsAHousehold"/> admits nobody who
    /// belongs to a house and the two passes above reach nobody who does not — the sets are
    /// disjoint by construction, and a notable who could also be ranked as a dynast would be a bug
    /// in that predicate rather than a tie for this one to break.
    /// </remarks>
    private static void RankNotables(WorldState world, DetMap<EntityId, int> ranks, int year)
    {
        foreach (Figure figure in world.Figures)
        {
            if (Offices.HeadsAHousehold(figure, year)) ranks[figure.Id] = NotableHeadRank;
        }
    }

    private static void Rank(DetMap<EntityId, int> ranks, List<Figure> line, int offset)
    {
        for (int i = 0; i < line.Count; i++)
        {
            int rank = i + offset;

            // A dynast standing in two lines takes the nearer throne's rank.
            if (!ranks.TryGetValue(line[i].Id, out int existing) || rank < existing)
            {
                ranks[line[i].Id] = rank;
            }
        }
    }

    private static void Marry(
        WorldState world, DetMap<EntityId, int> ranks, int year, IRng rng)
    {
        // Id order — birth order — so an older sibling is matched before a younger one, and the
        // pass does not depend on how the ranking happened to be assembled. Bounded to the figures
        // already on the books, since matching one adds their partner to the table behind us.
        int known = world.Figures.Count;

        for (int i = 0; i < known; i++)
        {
            Figure figure = world.Figures[i];
            if (!Marriageable(world, figure, ranks, year)) continue;
            if (!rng.Chance(MarriageChance)) continue;

            Culture culture = world.CultureOf(figure);

            // A standing lover is read before the political draw, not instead of it: the roll
            // below still fires even where none exists, and a figure with no courtship costs the
            // stream nothing more than the one it already spent. See MatchLover for why this is a
            // weighting rather than the political draw's replacement.
            Figure? beloved = MatchLover(world, figure, year, rng);
            Figure? found = beloved ?? FindPartner(world, figure, culture, year, rng);
            Figure partner = found ?? MatchAtHome(world, figure, culture, year, rng);

            // Not "did MatchLover supply this partner": the political draw just below it scans
            // every other house's unmarried dynasts and can land on the same person MatchLover
            // would have offered, if the weighted roll happened to fail first. Asking the question
            // this way — is the figure actually being married to their own open lover, however the
            // roll that produced this partner ran — is what keeps the chronicle's courtship voice
            // and the affinity's own Wed outcome reading the same fact, since
            // Affinities.ResolveCourtshipAtMarriage answers the identical question a few lines
            // below. Two independent guesses at "was this a courtship" would eventually disagree;
            // one shared answer cannot.
            bool courtship = Affinities.IsOpenLoverOf(figure, partner);

            Wed(world, figure, partner, ranks, year, courtship);

            // A spouse invented for this wedding takes their trade after it, not before. Chosen
            // first, they were an unmarried person who might reasonably enter holy orders, and
            // the wedding two lines later made them a married cleric in a faith that forbids
            // one — which is where most of the remaining violations came from once the vow
            // itself was fixed. An existing partner is untouched: they had a life already.
            if (found is null) Occupations.Ensure(world, partner, year);

            // Consumed or overridden after the wedding is on the books, not before: WhoMoves and
            // the chronicle both need the marriage to already be a fact, and a lover overridden by
            // this same figure's own wedding is exactly the case Affinities reads for it.
            Affinities.ResolveCourtshipAtMarriage(world, figure, partner, year);
        }
    }

    private static bool Marriageable(
        WorldState world, Figure figure, DetMap<EntityId, int> ranks, int year)
    {
        if (!figure.IsAlive || figure.IsMarried) return false;
        if (figure.AgeIn(year) < MarriageAge) return false;

        // Dynasts, and the heads of the households an office raised. A consort's own remarriage
        // would need a second house to be interested in them, and by then the chronicle is
        // following their children instead — which is also why a notable's spouse and children are
        // refused here and their household ends with the window that opened it.
        if (figure.DynastyId.IsNone && !Offices.HeadsAHousehold(figure, year)) return false;
        if (!InAStandingRealm(world, figure)) return false;
        if (VowedToCelibacy(world, figure)) return false;

        return ranks.TryGetValue(figure.Id, out int rank) && rank <= MarriageableRank;
    }

    /// <summary>
    /// Whether a figure lives somewhere a marriage could be made or a birth recorded.
    /// </summary>
    /// <remarks>
    /// A house whose realm has fallen keeps its members and its claims, but the chronicle has no
    /// seat to name and no court to hold — so it stops adding to itself until one of its own
    /// marries back into a realm that still stands.
    /// </remarks>
    private static bool InAStandingRealm(WorldState world, Figure figure) =>
        world.Civilizations.Contains(figure.CivilizationId)
        && world.Civilizations[figure.CivilizationId].IsActive;

    /// <summary>
    /// A sitting officer of a realm, other than a consort.
    /// </summary>
    /// <remarks>
    /// Consorts are created by marriage; pinning them would refuse the very move the office
    /// records. Everyone else holds a post that belongs to a realm, and moving them is how that
    /// post ends up in the wrong one.
    /// </remarks>
    private static bool PinnedByOffice(Figure figure)
    {
        OfficeHolding? held = figure.CurrentOffice;
        return held is not null && held.Kind is not OfficeKind.Consort;
    }

    /// <summary>
    /// Anyone in holy orders in a faith that forbids its clergy to marry.
    /// </summary>
    /// <remarks>
    /// <para><b>The vow binds the priesthood, not the seat.</b> This once asked only whether the
    /// figure held <see cref="OfficeKind.HighPriest"/>, which was defensible while clergy were
    /// only ever an office — one person per faith, and the rule reached all of them. M16 made the
    /// priesthood a population through <see cref="Occupation.Clergy"/> and the vow did not
    /// follow, so every ordinary priest was exempt from a rule their own faith's scripture
    /// asserts. Measured across six worlds, 20 of 267 clergy in celibate faiths had a spouse.</para>
    ///
    /// <para><b>The faith is the figure's own.</b> A cleric carries
    /// <see cref="Figure.ReligionId"/>, which is what the vow is owed to; the office's scope is
    /// consulted only for a high priest, whose seat may belong to a faith they have not otherwise
    /// professed. The vow is still the faith's rather than the person's, so a realm that changes
    /// religion mid-life releases them.</para>
    ///
    /// <para><b>The one case neither direction can catch</b> is a figure who marries and takes
    /// orders in a realm professing nothing, whom a celibate faith reaches years later. There was
    /// no vow to consult at either moment, so it is closed at the far end instead:
    /// <c>ReligionSystem.ConvertTheFaithless</c> declines to enrol them, and they stay faithless.
    /// <c>OccupationTests.NobodyInHolyOrdersIsMarriedWhereTheFaithForbidsIt</c> is what holds all
    /// three halves of the rule together.</para>
    ///
    /// <para>This refuses a marriage while the vow holds. It is not the whole of the rule:
    /// ordination is refused to the already-married in <see cref="Occupations.Choose"/> and
    /// <see cref="Offices.EligibleCleric"/>, because a vow that only ever fires in one direction
    /// would let the same person marry in one year and take orders in the next.</para>
    /// </remarks>
    internal static bool VowedToCelibacy(WorldState world, Figure figure)
    {
        OfficeHolding? held = figure.OpenOffice(OfficeKind.HighPriest);

        if (held is not null && world.Civilizations.Contains(held.CivilizationId))
        {
            EntityId seatFaith = held.ScopeId.IsNone
                ? world.FaithOf(world.Civilizations[held.CivilizationId])
                : held.ScopeId;

            if (Forbids(world, seatFaith)) return true;
        }

        return figure.Occupation == Occupation.Clergy && Forbids(world, figure.ReligionId);
    }

    /// <summary>Whether this faith forbids its clergy to marry.</summary>
    private static bool Forbids(WorldState world, EntityId faithId) =>
        !faithId.IsNone
        && world.Religions.Contains(faithId)
        && world.Religions[faithId].Character.CelibateClergy;

    /// <summary>
    /// Weights the marriage roll toward a figure's own standing courtship, where one survives.
    /// </summary>
    /// <remarks>
    /// <para>Issue #175: marriage used to end in <c>rng.Pick</c> over every unmarried dynast in the
    /// world, reading nothing the engine already held about the two people it joined. A figure who
    /// climbed <see cref="AffinityStage.Lover"/> with somebody carries exactly the kind of standing
    /// the political draw below has none of, so it is asked first — and the roll still keeps the
    /// political match live even where a lover exists, because an arranged marriage overriding a
    /// courtship is the historically correct outcome, not a bug this milestone is fixing.</para>
    ///
    /// <para>Eligibility is re-verified rather than trusted from the year the courtship climbed:
    /// years may have passed since two people became lovers, and a candidate a throne, an office,
    /// or a faith has since claimed is refused now even though it did not refuse them then — the
    /// same guards <see cref="FindPartner"/> already applies to its own candidates, asked of one
    /// figure instead of scanned across a world.</para>
    /// </remarks>
    private static Figure? MatchLover(WorldState world, Figure figure, int year, IRng rng)
    {
        FigureAffinity? lover = null;
        foreach (FigureAffinity affinity in figure.Affinities)
        {
            if (!affinity.IsOpen || affinity.Stage != AffinityStage.Lover) continue;
            lover = affinity;
            break;
        }

        if (lover is null) return null;

        EntityId candidateId = lover.Other(figure.Id);
        if (!world.Figures.Contains(candidateId)) return null;

        Figure candidate = world.Figures[candidateId];
        if (!candidate.IsAlive || candidate.IsMarried) return null;
        if (!InAStandingRealm(world, candidate)) return null;
        if (VowedToCelibacy(world, candidate)) return null;
        if (Succession.HoldsAThrone(world, candidate)) return null;
        if (PinnedByOffice(candidate)) return null;
        if (Succession.AreCloseKin(world, figure, candidate)) return null;

        // The one roll that is not FindPartner's abroad draw and not the parent MarriageChance
        // roll: whether this year's marriage is the one the courtship was climbed for, or whether
        // the house's own needs come first and the political draw below still gets its turn.
        return rng.Chance(CourtshipMarriageChance) ? candidate : null;
    }

    /// <summary>
    /// Weds every pair who found each other before the chronicle ever asked about them.
    /// </summary>
    /// <remarks>
    /// <para>Issue #309: a census across five seeds found <see cref="Marriageable"/> naming
    /// perhaps one figure in thirty as a candidate for marriage at all, while
    /// <see cref="Affinities"/> keeps forming <see cref="AffinityStage.Lover"/> ties among
    /// co-residents without asking anyone's rank. Of every pair that reached that rung across the
    /// census, not one had both members marriageable — the ladder was doing exactly what it was
    /// built to do, and the dynastic roll above had no way to ever see the result. Raising
    /// <see cref="MarriageableRank"/> to reach them would also reach every distant cousin the
    /// attention budget exists to forget, and inflating <see cref="CourtshipMarriageChance"/> does
    /// nothing for two people the roll above never considers in the first place. So this pass asks
    /// a narrower question than <see cref="Marry"/> does: not "should the house marry this person
    /// off", but "have these two already decided, and is there any remaining reason to refuse
    /// them" — the same reasons <see cref="Marriageable"/> checks, with the one about proximity to
    /// a throne left out, because nobody here was ever going to inherit one.</para>
    ///
    /// <para>Run as its own pass after <see cref="Marry"/> rather than folded into it, so that
    /// nobody is asked twice. A figure <see cref="Marriageable"/> already had a full turn above —
    /// their own <see cref="MatchLover"/> call included, win or lose against
    /// <see cref="CourtshipMarriageChance"/> — and <see cref="CourtshipReady"/> excludes them by
    /// construction rather than by remembering who already went. Anyone left standing here is
    /// left standing precisely because the pass above had no seat for them, not because it tried
    /// and failed.</para>
    ///
    /// <para>No roll decides whether either of these two marries: <see cref="MarriageChance"/>
    /// governs whether a house goes looking, and there is no house here doing any looking. Two
    /// people who already climbed a ladder this engine tracks specifically for its own sake need
    /// nothing further asked of the dice — only that nothing about either of them has changed
    /// since, which the guards below re-verify rather than assume.</para>
    ///
    /// <para>Bounded the same way <see cref="Marry"/> bounds itself: a couple wed here joins the
    /// figure table behind the walk, and is not itself a candidate for anything until next year.
    /// Neither partner gains a rank, a dynasty, or a household by marrying — <see cref="Wed"/>
    /// moves whichever of them has the weaker claim and nothing else — so <see cref="Bear"/> never
    /// sees them as fertile and no child of theirs is ever recorded, the same way none would have
    /// been had the political draw somehow reached them.</para>
    /// </remarks>
    private static void WedCourtships(WorldState world, DetMap<EntityId, int> ranks, int year)
    {
        // Id order, and bounded like Marry: a spouse this pass could invent would be a second
        // instance of the same bug MatchAtHome exists to avoid for the dynastic pass, except this
        // pass never invents one at all — every candidate here already exists, holding the other
        // half of a standing tie the ladder recorded years, sometimes decades, ago.
        int known = world.Figures.Count;

        for (int i = 0; i < known; i++)
        {
            Figure figure = world.Figures[i];
            if (!CourtshipReady(world, ranks, figure, year)) continue;

            EntityId? loverId = Affinities.OpenLoverId(figure);
            if (loverId is not EntityId candidateId) continue;
            if (!world.Figures.Contains(candidateId)) continue;

            Figure candidate = world.Figures[candidateId];

            // Re-verified rather than trusted, on the same reasoning MatchLover already gives for
            // its own candidate: years may separate the climb from this tick, and a vow taken or a
            // household inherited since would refuse this marriage today even though nothing
            // refused the courtship then. Close kin and opposite sexes cannot actually have
            // changed since CourtshipEligible already gated the climb on both — kept here anyway
            // so this pass never trusts a guard it did not itself just ask.
            if (!CourtshipReady(world, ranks, candidate, year)) continue;
            if (figure.Sex == candidate.Sex) continue;
            if (Succession.AreCloseKin(world, figure, candidate)) continue;

            // Same-house matches are refused here for the identical reason FindPartner refuses
            // them for the dynastic pass: both partners would land in one line of succession and
            // be counted twice by everything downstream that walks a house. It is the one guard
            // Marriageable never had to state, because Marriageable never let two members of the
            // same house reach this pairing in the first place — a dynast within
            // MarriageableRank only ever meets candidates FindPartner drew from other houses. This
            // pass draws no candidates at all, it reads whichever pair the ladder already formed,
            // and two distant cousins of the same dynasty — both well past MarriageableRank, both
            // still co-residents Affinities tracks — are exactly the pair that guard was never
            // asked to consider.
            if (!figure.DynastyId.IsNone && figure.DynastyId == candidate.DynastyId) continue;

            Wed(world, figure, candidate, ranks, year, courtship: true);

            // Closes the tie as AffinityOutcome.Wed, the same call the dynastic pass makes right
            // after its own Wed — one shared answer to "was this a courtship" rather than a second
            // guess this file would eventually let drift from the first.
            Affinities.ResolveCourtshipAtMarriage(world, figure, candidate, year);
        }
    }

    /// <summary>
    /// The guards <see cref="Marriageable"/> applies, minus the one that exists only to bound the
    /// dynastic chronicle's own attention.
    /// </summary>
    /// <remarks>
    /// Alive, unmarried, of age, in a realm still standing, and not under a vow — everything
    /// <see cref="Marriageable"/> asks before it ever gets to <see cref="Figure.DynastyId"/> or
    /// <see cref="MarriageableRank"/>. Refusing anyone <see cref="Marriageable"/> already accepts
    /// is what keeps this pass from being a second, dice-free route into a marriage the dynastic
    /// roll is still deciding this same year; everyone left is left because that check found no
    /// seat for them at all, not because this year's roll had not gotten to them yet.
    /// </remarks>
    private static bool CourtshipReady(
        WorldState world, DetMap<EntityId, int> ranks, Figure figure, int year)
    {
        if (!figure.IsAlive || figure.IsMarried) return false;
        if (figure.AgeIn(year) < MarriageAge) return false;
        if (!InAStandingRealm(world, figure)) return false;
        if (VowedToCelibacy(world, figure)) return false;

        return !Marriageable(world, figure, ranks, year);
    }

    /// <summary>
    /// Looks for a match among the other houses.
    /// </summary>
    /// <remarks>
    /// <para>This is where a marriage becomes more than bookkeeping: it ties two houses together,
    /// moves a person between realms, and — when the line later runs through them — carries one
    /// house's claim into another's territory. <see cref="CultureValues.Mercantile"/> decides how
    /// outward-looking a people is about it, so a trading culture's family tree reaches across the
    /// map and an insular one marries its own neighbours.</para>
    ///
    /// <para>Same-house matches are refused outright rather than merely discouraged. Permitting
    /// them would put both partners in one line of succession, and a couple would then be counted
    /// twice by everything downstream that walks a house.</para>
    /// </remarks>
    private static Figure? FindPartner(
        WorldState world, Figure figure, Culture culture, int year, IRng rng)
    {
        // A household of no house looks no further than home, and this is the guard that keeps the
        // raised path bounded. Every candidate below is a member of a dynasty, so matching a
        // notable here would put their children in a line of succession — after which they are
        // ranked by proximity to a throne rather than by the window that raised them, and the level
        // shift the design bought becomes a growth rate. Refused before the roll, so a notable's
        // marriage costs the stream nothing a dynast's would not.
        if (figure.DynastyId.IsNone) return null;

        double abroad = ForeignMatchFloor + ForeignMatchFromTrade * culture.Values.Mercantile;
        if (!rng.Chance(abroad)) return null;

        var candidates = new List<Figure>();

        foreach (Dynasty house in world.Dynasties)
        {
            if (house.Id == figure.DynastyId) continue;

            foreach (EntityId id in house.MemberIds)
            {
                Figure candidate = world.Figures[id];

                if (!candidate.IsAlive || candidate.IsMarried) continue;
                if (candidate.Sex == figure.Sex) continue;

                int age = candidate.AgeIn(year);
                if (age < MarriageAge || age > LastChildbearingAge) continue;

                if (!InAStandingRealm(world, candidate)) continue;
                if (VowedToCelibacy(world, candidate)) continue;

                // A reigning ruler is not available to marry into someone else's realm: one of the
                // pair would have to move, and a crowned head that moves is a personal union, which
                // belongs to Milestone 6's diplomacy rather than to a marriage roll. It also fixes
                // an opening-year artefact — every founding ruler is unmarried in year one, so
                // without this the eight of them pair off with each other and half the founding
                // houses are absorbed into the other half before the chronicle has begun.
                if (Succession.HoldsAThrone(world, candidate)) continue;
                if (PinnedByOffice(candidate)) continue;

                if (Succession.AreCloseKin(world, figure, candidate)) continue;

                candidates.Add(candidate);
            }
        }

        return candidates.Count == 0 ? null : rng.Pick(candidates);
    }

    /// <summary>A partner of no recorded house, from the figure's own realm.</summary>
    private static Figure MatchAtHome(
        WorldState world, Figure figure, Culture culture, int year, IRng rng)
    {
        Civilization civilization = world.Civilizations[figure.CivilizationId];
        Sex sex = figure.Sex == Sex.Male ? Sex.Female : Sex.Male;

        // No career yet: the caller gives them one after the wedding, so that the vow of a
        // celibate faith sees a married person rather than a momentarily single one.
        return Houses.NewFigure(
            world, civilization, culture, sex, year - rng.NextInt(MarriageAge, 27));
    }

    /// <summary>
    /// Children who have reached majority take a career, once.
    /// </summary>
    /// <remarks>
    /// Walks the whole table rather than the marriage roster, because a notable's children are
    /// recorded and then drop out of the line — they still live, and a vacant seat is entitled
    /// to know what they became. Forked per figure, so the walk itself draws nothing.
    /// </remarks>
    private static void ComeOfAge(WorldState world, int year)
    {
        foreach (Figure figure in world.Figures)
        {
            Occupation before = figure.Occupation;
            if (figure.AgeIn(year) == Succession.MajorityAge)
            {
                Upbringings.EndAtMajority(world, figure, year);
                Figure? mentor = before == Occupation.None
                    ? Upbringings.FindMentor(world, figure, year)
                    : null;
                if (mentor is not null)
                {
                    LifeStories.AddMentorship(
                        mentor,
                        figure,
                        year,
                        Upbringings.FamilyOf(mentor.Occupation),
                        Upbringings.MentorshipLocation(world, figure, mentor));
                }
            }

            Occupations.Ensure(world, figure, year);
        }
    }

    /// <summary>
    /// Records a marriage, moving whichever partner has the weaker claim.
    /// </summary>
    /// <remarks>
    /// Weaker claim rather than sex, so a foreign prince marrying a reigning queen moves to her
    /// realm as readily as the reverse — and his house, being his children's, arrives with him.
    /// Id breaks the tie so the choice never depends on iteration order.
    /// </remarks>
    private static void Wed(
        WorldState world,
        Figure figure,
        Figure partner,
        DetMap<EntityId, int> ranks,
        int year,
        bool courtship)
    {
        // Read before the move below rewrites one of them. A match made across a frontier is a
        // fact about two realms and belongs in both their histories; the same wedding read after
        // the bride or groom has changed allegiance looks domestic.
        bool crossedRealms = figure.CivilizationId != partner.CivilizationId;

        figure.SpouseId = partner.Id;
        partner.SpouseId = figure.Id;
        figure.SpouseIds.Add(partner.Id);
        partner.SpouseIds.Add(figure.Id);

        Figure mover = WhoMoves(world, figure, partner, ranks);
        Figure stays = mover.Id == figure.Id ? partner : figure;
        mover.CivilizationId = stays.CivilizationId;

        // And they move to where their partner actually lives, not merely into their realm. A
        // governor's spouse belongs in the town he governs; leaving them at the capital would put
        // a household in two places and expose the two halves of it to different disasters.
        EntityId household = world.ResidenceOf(stays);
        Houses.Settle(world, mover, household, ResidenceReason.Marriage, year);

        // Chronicled where the household is, not at the realm's seat. A wedding recorded at the
        // capital puts a provincial couple in a town neither of them lives in, and every later
        // line that does know where they live — a siege they endured, a journey they set out on —
        // then reads as though they had appeared there from nowhere.
        //
        // The courtship voice is the one thing that tells this marriage apart from an arranged
        // one in the chronicle: same event kind, same facts, a different key into Narration so a
        // reader can tell a match the two of them made from one their houses made for them.
        world.Chronicle.Record(
            year,
            EventKind.FigureMarried,
            figure.Id,
            obj: partner.Id,
            location: household,
            extra: HousesJoined(figure, partner),
            data: courtship ? Chronicle.Data((Narration.VoiceDataKey, "courtship")) : null,
            significance:
                Houses.HeldPower(figure) || Houses.HeldPower(partner)
                || (crossedRealms && (ReignsIn(world, figure) || ReignsIn(world, partner)))
                    ? Significance.Notable
                    : Significance.Routine);

        LifeStories.Marry(world, figure, partner, year);
    }

    /// <summary>
    /// Whether a figure belongs to the house currently sitting on their realm's throne.
    /// </summary>
    /// <remarks>
    /// The test that makes a marriage across a frontier diplomacy rather than migration. Two
    /// commoners of different realms marrying is a household moving; a reigning house marrying
    /// into a neighbour is the kind of match that later gives someone a claim, and the succession
    /// system will read exactly this relationship when it does. Everyone marries somebody, so
    /// without the reigning-house test a third of every chronicle is weddings.
    /// </remarks>
    private static bool ReignsIn(WorldState world, Figure figure)
    {
        if (figure.DynastyId.IsNone) return false;
        if (!world.Civilizations.Contains(figure.CivilizationId)) return false;

        Civilization realm = world.Civilizations[figure.CivilizationId];
        return world.Figures.Contains(realm.CurrentRulerId)
            && world.Figures[realm.CurrentRulerId].DynastyId == figure.DynastyId;
    }

    /// <summary>
    /// Which partner leaves home. The weaker claim, and never a crowned head or a sitting officer.
    /// </summary>
    /// <remarks>
    /// <para>The throne check is not a nicety. A ruler moved out of the realm they rule leaves
    /// <see cref="Civilization.CurrentRulerId"/> pointing at someone who now lives somewhere else,
    /// and <see cref="Houses.Die"/> finds the civilization to vacate through the dead figure's own
    /// residence — so their death empties the wrong throne and leaves the right one occupied by a
    /// corpse. Succession sees a realm that has a ruler and skips it, for ever. Three realms in a
    /// three-century run were governed by the dead for over a century each before this, and
    /// nothing in the chronicle said so: the events simply stopped.</para>
    ///
    /// <para>The same shape applies to every other office. A high priest of a bloodline faith is a
    /// dynast, so they enter the marriage pool that invented clergy never do, and moving them
    /// leaves the seat in one realm and its holder in another. A governor is worse: they live in
    /// the town they govern, and a marriage that changed their civilization would not change the
    /// town.</para>
    /// </remarks>
    private static Figure WhoMoves(
        WorldState world, Figure a, Figure b, DetMap<EntityId, int> ranks)
    {
        if (Succession.HoldsAThrone(world, a) || PinnedByOffice(a)) return b;
        if (Succession.HoldsAThrone(world, b) || PinnedByOffice(b)) return a;

        int rankA = ranks.TryGetValue(a.Id, out int found) ? found : int.MaxValue;
        int rankB = ranks.TryGetValue(b.Id, out int other) ? other : int.MaxValue;

        if (rankA != rankB) return rankA > rankB ? a : b;
        return a.Id.CompareTo(b.Id) > 0 ? a : b;
    }

    /// <summary>The houses a marriage joins, so the event lands on both of their pages.</summary>
    private static EntityId[]? HousesJoined(Figure a, Figure b)
    {
        if (a.DynastyId.IsNone && b.DynastyId.IsNone) return null;
        if (b.DynastyId.IsNone || b.DynastyId == a.DynastyId) return new[] { a.DynastyId };
        if (a.DynastyId.IsNone) return new[] { b.DynastyId };

        return new[] { a.DynastyId, b.DynastyId };
    }

    /// <summary>
    /// Children, walked over mothers rather than couples.
    /// </summary>
    /// <remarks>
    /// A couple has exactly one mother and may have partners drawn from two houses, so iterating
    /// mothers is what guarantees each household is considered once a year. Iterating houses
    /// instead would give a cross-house couple two chances at a child and none of the obvious
    /// guards against it survive contact with a house that rules two realms.
    /// </remarks>
    private static void Bear(
        WorldState world, DetMap<EntityId, int> ranks, int year, IRng rng)
    {
        // Bounded like the marriage pass: a child born this year joins the table behind us, and is
        // not a candidate for anything until the next.
        int known = world.Figures.Count;

        for (int i = 0; i < known; i++)
        {
            Figure mother = world.Figures[i];
            if (mother.Sex != Sex.Female || !mother.IsAlive || !mother.IsMarried) continue;
            if (!InAStandingRealm(world, mother)) continue;

            int age = mother.AgeIn(year);
            if (age < MarriageAge || age > LastChildbearingAge) continue;

            Figure father = world.Figures[mother.SpouseId];
            if (!father.IsAlive) continue;

            if (!NearEnoughToTheThrone(ranks, mother, father)) continue;
            if (!rng.Chance(BirthChance * Fecundity(age))) continue;

            Deliver(world, mother, father, year, rng);
        }
    }

    private static bool NearEnoughToTheThrone(
        DetMap<EntityId, int> ranks, Figure mother, Figure father)
    {
        bool near = ranks.TryGetValue(mother.Id, out int hers) && hers <= FertileRank;
        return near || (ranks.TryGetValue(father.Id, out int his) && his <= FertileRank);
    }

    /// <summary>
    /// How likely a birth is at a given age, as a fraction of the base rate.
    /// </summary>
    /// <remarks>
    /// Piecewise-linear rather than a curve: the shape wanted here is a plateau through the
    /// twenties falling away through the thirties, and every smooth function that produces it
    /// needs a transcendental this engine will not use on a decision path.
    /// </remarks>
    private static double Fecundity(int age)
    {
        if (age < 20) return DetMath.Lerp(0.45, 1.0, DetMath.InverseLerp(16.0, 20.0, age));
        if (age <= 32) return 1.0;

        return DetMath.Lerp(1.0, 0.12, DetMath.InverseLerp(32.0, LastChildbearingAge, age));
    }

    private static void Deliver(
        WorldState world, Figure mother, Figure father, int year, IRng rng)
    {
        // The child takes the father's house, and the mother's only where there is no father's
        // house to take — which is what lets a queen's line continue when her consort has none.
        Figure heirOf = father.DynastyId.IsNone ? mother : father;
        Civilization civilization = world.Civilizations[mother.CivilizationId];
        Culture culture = world.CultureOf(heirOf);

        // Born where their mother lives, not where the court sits. It resolves to the capital for
        // everyone who is at it, so this changes nothing for a dynasty — but a governor's household
        // lives in the town they govern, and a child of it recorded as born at the capital would
        // take the capital's faith as well as its name. Households an office raised are mostly
        // provincial, which is what makes the distinction start to matter here.
        EntityId home = world.ResidenceOf(mother);

        Sex sex = rng.Chance(0.5) ? Sex.Male : Sex.Female;
        Figure child = Houses.NewFigure(
            world, civilization, culture, sex, year,
            birthSettlementId: home, mother: mother, father: father);

        child.MotherId = mother.Id;
        child.FatherId = father.Id;
        child.DynastyId = heirOf.DynastyId;

        mother.ChildIds.Add(child.Id);
        father.ChildIds.Add(child.Id);

        LifeStories.AddParent(world, mother, child, year);
        LifeStories.AddParent(world, father, child, year);

        if (world.Dynasties.Contains(child.DynastyId))
        {
            world.Dynasties[child.DynastyId].MemberIds.Add(child.Id);
        }

        // A birth is history when it lands in the line of succession, and only then. Nothing is
        // known about a newborn except who their parents are, so the crown is the one thing about
        // them that can matter on the day: an heir changes what happens when a throne next falls
        // vacant. A governor's child does not, and there are far more of those — extending this to
        // every office put four hundred nurseries back into the chronicle and buried the wars
        // again. The child's own page carries the birth either way.
        bool bornToThrone =
            father.Holds(OfficeKind.Ruler) || father.Holds(OfficeKind.Regent)
            || mother.Holds(OfficeKind.Ruler) || mother.Holds(OfficeKind.Regent);

        // The mother is named through {extra:fig}, which resolves to the first figure among the
        // extras. Subject and object are spoken for — the child and the father — and carrying her
        // as a data string instead would print her name as flat text, so the one person certainly
        // present at a birth would be the only one in the sentence that is not a link.
        world.Chronicle.Record(
            year,
            EventKind.FigureBorn,
            child.Id,
            obj: father.Id,
            location: home,
            extra: child.DynastyId.IsNone
                ? new[] { mother.Id }
                : new[] { mother.Id, child.DynastyId },
            data: Chronicle.Data(("child", sex == Sex.Male ? "son" : "daughter")),
            significance: bornToThrone ? Significance.Notable : Significance.Routine);

        if (rng.Chance(ChildbedRisk))
        {
            Houses.Die(world, mother, year, DeathCause.Childbirth);
        }
    }
}
