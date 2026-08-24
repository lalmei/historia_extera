using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Fills the offices below a throne, and empties them again.
/// </summary>
/// <remarks>
/// <para><b>This is a use for people the world already pays for.</b> Before it, of 831 figures who
/// reached sixteen on seed 42, 658 never held an office of any kind — and 380 of those were
/// dynasts, the cadets the attention budget deliberately breeds so that houses do not fail and
/// then has nothing to do with. They lived some twenty thousand recorded adult years between them
/// in which nothing happened but a marriage, some children, and a death.</para>
///
/// <para><b>Every office here changes what some other system does.</b> That is the standard
/// <see cref="CultureValues.Tradition"/> failed until M4: a field read by nothing is decoration,
/// and decoration in a chronicle reads as noise. A marshal is preferred by
/// <c>Warfare.Commander</c>, a governor lives in their town and dies in its sack, a high priest
/// replaces the placeholder that picked whichever adult happened to be oldest, and a crowned
/// consort is the realm's first regent.</para>
///
/// <para>Runs after the houses, so a ruler crowned this spring makes their appointments in the
/// same year and against a line of succession that has already settled.</para>
///
/// <para>Samples no terrain.</para>
/// </remarks>
public sealed class OfficeSystem : ISystem
{
    /// <summary>Yearly chance a vacant seat is actually filled.</summary>
    /// <remarks>
    /// Not one. A court does not fill every post the instant it falls vacant, and a realm that did
    /// would have its entire office roster turn over in the single year a reign ends — which reads
    /// as an administrative reshuffle rather than as history.
    /// </remarks>
    private const double FillChance = 0.35;

    /// <summary>Smallest tier of settlement the crown bothers to appoint anyone to.</summary>
    /// <remarks>
    /// The same line <c>MaybeFortify</c> draws, and not a coincidence worth hiding: Town is the
    /// size at which this engine starts treating a place as somewhere with interests of its own.
    /// </remarks>
    private const SettlementTier GovernedFrom = SettlementTier.Town;

    /// <summary>Yearly chance a failing marshal or governor is stripped of their office.</summary>
    private const double DisgraceChance = 0.05;

    /// <summary>How far from the capital counts as the far side of the realm.</summary>
    private const double ReachRange = 2200.0;

    /// <summary>How much more a realm at war insists on naming its own officers.</summary>
    private const double WartimeMandate = 1.3;

    /// <summary>Odds a family keeps a seat it already held, at no <see cref="CultureValues.Tradition"/>.</summary>
    private const double CustomaryFloor = 0.15;

    /// <summary>The same, at full Tradition.</summary>
    /// <remarks>
    /// Well short of certainty, and deliberately. An office that always passes to the last holder's
    /// child stops being a decision, and a realm whose every seat is hereditary has no appointments
    /// left to read about — so Tradition weights the custom rather than deciding it, and the most
    /// traditional people in the world still fills three seats in ten some other way.
    /// </remarks>
    private const double CustomaryCeiling = 0.70;

    /// <summary>How far a wholly centralising ruler can push the custom down.</summary>
    private const double CustomaryUnderTheCrown = 0.35;

    public string Name => "offices";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);

            // One stream per realm, so the number of offices one court fills cannot shift what
            // another court does in the same year.
            IRng court = rng.Fork("court", civilization.Id.ToDiscriminator());

            Release(world, civilization, year);
            FillRealmOffices(world, civilization, culture, year, court);
            FillGovernorships(world, civilization, culture, year, court);
            Disgrace(world, civilization, year, court);
        }
    }

    // -----------------------------------------------------------------------
    // Endings
    // -----------------------------------------------------------------------

    /// <summary>
    /// Closes offices that have quietly ceased to exist.
    /// </summary>
    /// <remarks>
    /// Four ways, none of which is an event: the holder left the realm or died, a mandated
    /// appointment's grantor is no longer governing, the settlement governed is gone or has changed
    /// hands, or the faith served has faded. The commonest is the second — appointments are
    /// personal to the ruler who made them, which is what keeps the roster turning over instead of
    /// freezing after the first generation.
    /// </remarks>
    private static void Release(WorldState world, Civilization civilization, int year)
    {
        foreach (Figure figure in world.Figures)
        {
            // Every open office this realm granted, not merely the newest. Scanning only the most
            // recent is unambiguous exactly while nobody can hold two at once, which is a property
            // of today's grant rules rather than of this loop — the same assumption that left a
            // regent recorded as governing for three centuries after he died.
            OfficeHolding? held = OpenHere(figure, civilization);
            if (held is null) continue;
            if (held.Kind is OfficeKind.Ruler or OfficeKind.Regent) continue;

            if (!figure.IsAlive || figure.CivilizationId != civilization.Id)
            {
                Offices.Lapse(world, figure, held.Kind, year);
                continue;
            }

            if (!held.GrantedBy.IsNone && !StillGoverning(world, civilization, held.GrantedBy))
            {
                Offices.Lapse(world, figure, held.Kind, year);
                continue;
            }

            if (held.Kind == OfficeKind.Governor && !GovernorshipStands(world, civilization, held))
            {
                Offices.Lapse(world, figure, held.Kind, year);
                continue;
            }

            if (held.Kind == OfficeKind.HighPriest && world.FaithOf(civilization) != held.ScopeId)
            {
                Offices.Lapse(world, figure, held.Kind, year);
                continue;
            }

            // A consort is the ruler's spouse. Widowhood ends the office, not the marriage's memory.
            if (held.Kind == OfficeKind.Consort
                && (civilization.CurrentRulerId.IsNone || figure.SpouseId != civilization.CurrentRulerId))
            {
                Offices.Lapse(world, figure, held.Kind, year);
            }
        }
    }

    /// <summary>The first open office this figure holds of this realm, in the order granted.</summary>
    private static OfficeHolding? OpenHere(Figure figure, Civilization civilization)
    {
        foreach (OfficeHolding held in figure.Offices)
        {
            if (held.ToYear is null && held.CivilizationId == civilization.Id) return held;
        }

        return null;
    }

    private static bool StillGoverning(
        WorldState world, Civilization civilization, EntityId grantor) =>
        civilization.CurrentRulerId == grantor || civilization.RegentId == grantor;

    private static bool GovernorshipStands(
        WorldState world, Civilization civilization, OfficeHolding held)
    {
        if (!world.Settlements.Contains(held.ScopeId)) return false;

        Settlement place = world.Settlements[held.ScopeId];

        // A capital is governed by whoever holds the throne, in person — so a town that becomes
        // one has no governor. Capitals move: the succession system repoints a realm whose seat
        // was abandoned or taken to its largest surviving settlement, which is exactly the town
        // most likely to have had a governor sitting in it.
        return place.IsActive && place.CivilizationId == civilization.Id && !place.IsCapital;
    }

    // -----------------------------------------------------------------------
    // Filling
    // -----------------------------------------------------------------------

    private static void FillRealmOffices(
        WorldState world, Civilization civilization, Culture culture, int year, IRng court)
    {
        // A realm with no seat of government is in no position to appoint anyone to anything.
        if (!world.Settlements.Contains(civilization.CapitalId)) return;

        if (!Held(world, civilization, OfficeKind.Marshal) && court.Chance(FillChance))
        {
            Appoint(world, civilization, culture, OfficeKind.Marshal, EntityId.None, year, court);
        }

        EntityId faith = world.FaithOf(civilization);
        if (!faith.IsNone
            && !Held(world, civilization, OfficeKind.HighPriest)
            && court.Chance(FillChance))
        {
            Appoint(world, civilization, culture, OfficeKind.HighPriest, faith, year, court);
        }

        Crown(world, civilization, culture, year);
    }

    /// <summary>
    /// Recognises a ruler's spouse, where the government has such a thing.
    /// </summary>
    /// <remarks>
    /// <para>Not an appointment: nobody is chosen, so there is no fill mode and no roll. It is the
    /// ruler's own marriage being given a public standing, and the interesting decision is whether
    /// the realm has the office at all. A republic does not crown a consul's wife, so a republic's
    /// chronicle has no queens — which is content rather than an omission.</para>
    ///
    /// <para>What it buys: the crowned consort's tie to their birth house counts for more than any
    /// other marriage between the two, and a consort who outlives the ruler is the dowager the
    /// regency reaches for first.</para>
    /// </remarks>
    private static void Crown(
        WorldState world, Civilization civilization, Culture culture, int year)
    {
        if (culture.Government is not (GovernmentForm.Monarchy
            or GovernmentForm.Chiefdom
            or GovernmentForm.Theocracy))
        {
            return;
        }

        if (!world.Figures.Contains(civilization.CurrentRulerId)) return;

        Figure ruler = world.Figures[civilization.CurrentRulerId];
        if (!world.Figures.Contains(ruler.SpouseId)) return;

        Figure consort = world.Figures[ruler.SpouseId];
        if (!consort.IsAlive || consort.CurrentOffice is not null) return;

        // A sovereign in their own right keeps their own style; two crowns do not make a consort.
        if (Succession.HoldsAThrone(world, consort)) return;

        // And they have to live here. Enthrone brings a consort home with the crown, but makes
        // one deliberate exception — a spouse who holds a throne of their own stays in their own
        // court, because a ruler resident in a realm they do not rule routes their death to the
        // wrong throne. Recognising them here anyway gave one figure an office of a realm they had
        // never lived in, re-granted every year as the release pass threw it straight back, which
        // is three chronicle entries a year saying nothing.
        if (consort.CivilizationId != civilization.Id) return;

        Offices.Grant(
            world,
            civilization,
            culture,
            consort,
            OfficeKind.Consort,
            EntityId.None,
            ruler.Id,
            "by marriage to the crown",
            year);
    }

    private static void FillGovernorships(
        WorldState world, Civilization civilization, Culture culture, int year, IRng court)
    {
        foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
        {
            // The capital is governed by whoever holds the throne, in person.
            if (settlement.IsCapital || settlement.Tier < GovernedFrom) continue;
            if (GovernorOf(world, settlement) is not null) continue;

            // Forked per settlement, so a realm's twelfth town is not decided by how its first
            // eleven went — and adding a town cannot shift the appointments of the others.
            IRng local = court.Fork("town", settlement.Id.ToDiscriminator());
            if (!local.Chance(FillChance)) continue;

            Appoint(
                world, civilization, culture, OfficeKind.Governor, settlement.Id, year, local);
        }
    }

    /// <summary>
    /// Puts somebody in a seat, by whichever route this realm fills that seat.
    /// </summary>
    private static void Appoint(
        WorldState world,
        Civilization civilization,
        Culture culture,
        OfficeKind kind,
        EntityId scope,
        int year,
        IRng rng)
    {
        Figure? governing = Governing(world, civilization);
        FillMode mode = ChooseMode(world, civilization, culture, kind, scope, governing, rng);

        Figure? holder;
        EntityId grantedBy = EntityId.None;

        if (mode == FillMode.Mandated && governing is not null)
        {
            List<Figure> candidates = Eligible(world, civilization, kind, Offices.Courtiers(world, civilization, year));
            holder = PickCandidate(candidates, kind, rng);
            if (holder is not null) grantedBy = governing.Id;
        }
        else if (kind == OfficeKind.HighPriest && BloodlineFaith(world, civilization))
        {
            // A bloodline clergy draws its priests from the ruling house: the court supplies a
            // dynast, and the chronicle says the body chose. That is a different claim from the
            // Customary one below, which is a family keeping a seat it already held rather than a
            // house holding every seat of its faith — so this branch runs first and a bloodline
            // faith whose court has nobody to spare still falls through to the last priest's heir.
            List<Figure> candidates = Eligible(world, civilization, kind, Offices.Courtiers(world, civilization, year));
            holder = PickCandidate(candidates, kind, rng);
        }
        else
        {
            holder = null;
        }

        // The family's claim, tried only where the crown did not name somebody. That ordering is
        // what makes "the crown acquiesces" true of the model rather than only of the prose: a
        // ruler who wanted this seat has already taken it, and what is left is a post the court
        // was going to fill locally anyway.
        if (holder is null)
        {
            Figure? heir = Offices.HeirTo(world, civilization, kind, scope, year);

            if (heir is not null && rng.Chance(CustomaryOdds(culture, governing)))
            {
                holder = heir;
                mode = FillMode.Customary;
            }
        }

        // The court could not or would not supply anyone and no family had a claim on it, so the
        // body finds its own. That is the only path that creates a figure, and it creates one of no
        // house — which is what keeps a raised notable's household a level shift. See
        // Offices.Notable.
        holder ??= Offices.Notable(
            world,
            civilization,
            culture,
            kind,
            kind == OfficeKind.Governor ? scope : civilization.CapitalId,
            year,
            rng);

        // A grant with no grantor is one the crown did not make, which is Internal — unless a
        // family's claim is what filled it. The third mode keeps its own name through here because
        // "a body chose its own" and "a house kept what it held" are the same row otherwise.
        if (grantedBy.IsNone && mode != FillMode.Customary) mode = FillMode.Internal;

        Offices.Grant(
            world, civilization, culture, holder, kind, scope, grantedBy,
            ClaimFor(mode, kind, culture), year);
    }

    /// <summary>
    /// Whether the crown names this holder or the body finds its own.
    /// </summary>
    /// <remarks>
    /// <para>The substance of the model. The crown's inclination to mandate rises with the
    /// governing person's own <see cref="Disposition.Centralism"/>, falls with the culture's
    /// Tradition — a people attached to its own customs resents an appointee — rises while the
    /// realm is at war, falls with distance from the capital, and rises sharply for a settlement
    /// the realm has held for less than a lifetime.</para>
    ///
    /// <para>An army is the one thing no ruler delegates by custom, so a marshal leans mandated
    /// whatever else is true. A faith that lets the crown name its priests is a faith the crown has
    /// absorbed, so a high priest leans the other way.</para>
    /// </remarks>
    private static FillMode ChooseMode(
        WorldState world,
        Civilization civilization,
        Culture culture,
        OfficeKind kind,
        EntityId scope,
        Figure? governing,
        IRng rng)
    {
        if (governing is null) return FillMode.Internal;

        double mandate = governing.Disposition.Centralism;

        mandate *= kind switch
        {
            OfficeKind.Marshal => 1.4,
            OfficeKind.HighPriest => 0.6 * PriestlyMandate(world, civilization),
            _ => 1.0,
        };

        mandate *= DetMath.Lerp(1.25, 0.65, culture.Values.Tradition);

        // A realm at war centralises. Everything a crown delegates in peacetime is a thing it
        // wants in a reliable hand while there is a campaign to lose, and this is the input the
        // design named that the first cut of this method left out.
        if (AtWar(world, civilization.Id)) mandate *= WartimeMandate;

        if (world.Settlements.Contains(scope))
        {
            Settlement place = world.Settlements[scope];
            Settlement seat = world.Settlements[civilization.CapitalId];

            double reach = DetMath.Clamp01(
                world.Distance(seat.X, seat.Z, place.X, place.Z) / ReachRange);

            // The far side of the realm governs itself, because enforcing anything else costs more
            // than the appointment is worth.
            mandate *= DetMath.Lerp(1.25, 0.60, reach);

            // Unless it was taken, or planted, within living memory — then the crown insists.
            if (place.FoundedBy != civilization.Id || place.FoundedYear > world.Year - 60)
            {
                mandate *= 1.6;
            }
        }

        return rng.Chance(DetMath.Clamp01(mandate)) ? FillMode.Mandated : FillMode.Internal;
    }

    /// <summary>
    /// How likely a family is to keep a seat one of them already held.
    /// </summary>
    /// <remarks>
    /// <para>Where a hereditary local gentry comes from, and the pressure a centralising ruler
    /// eventually pushes against. Tradition is the people's attachment to their own customs, and an
    /// office running in a family is the most concrete custom this engine has; Centralism is the
    /// crown's appetite for deciding things itself, and it is read here from the same person and
    /// the same year that <see cref="ChooseMode"/> read it from.</para>
    ///
    /// <para>A realm with no ruler applies the custom undiminished. There is nobody to acquiesce,
    /// which is exactly the circumstance in which what a town has always done is what a town
    /// does.</para>
    /// </remarks>
    private static double CustomaryOdds(Culture culture, Figure? governing)
    {
        double odds = DetMath.Lerp(CustomaryFloor, CustomaryCeiling, culture.Values.Tradition);

        if (governing is not null)
        {
            odds *= DetMath.Lerp(1.0, CustomaryUnderTheCrown, governing.Disposition.Centralism);
        }

        return DetMath.Clamp01(odds);
    }

    private static double PriestlyMandate(WorldState world, Civilization civilization)
    {
        EntityId faithId = world.FaithOf(civilization);
        if (faithId.IsNone || !world.Religions.Contains(faithId)) return 1.0;

        return world.Religions[faithId].Character.PriestlyMandate();
    }

    private static bool BloodlineFaith(WorldState world, Civilization civilization)
    {
        EntityId faithId = world.FaithOf(civilization);
        return !faithId.IsNone
               && world.Religions.Contains(faithId)
               && world.Religions[faithId].Character.Clergy == ClergyAdmission.Bloodline;
    }

    private static List<Figure> Eligible(
        WorldState world, Civilization civilization, OfficeKind kind, List<Figure> candidates)
    {
        if (kind != OfficeKind.HighPriest) return candidates;

        var admitted = new List<Figure>(candidates.Count);
        foreach (Figure figure in candidates)
        {
            if (Offices.EligibleCleric(world, civilization, figure)) admitted.Add(figure);
        }

        return admitted;
    }

    /// <summary>Battle renown is a qualification for marshal; other offices keep their own rules.</summary>
    private static Figure? PickCandidate(List<Figure> candidates, OfficeKind kind, IRng rng)
    {
        if (candidates.Count == 0) return null;
        if (kind != OfficeKind.Marshal) return rng.Pick(candidates);

        Figure? best = null;
        double bestScore = double.NegativeInfinity;
        foreach (Figure candidate in candidates)
        {
            // A per-candidate tie-break means adding a courtier does not reshuffle everyone else.
            double score = Campaigns.Renown(candidate)
                + rng.Fork("marshal-candidate", candidate.Id.ToDiscriminator()).NextDouble();
            if (score > bestScore
                || (score == bestScore && best is not null && candidate.Id.CompareTo(best.Id) < 0))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static string ClaimFor(FillMode mode, OfficeKind kind, Culture culture) => mode switch
    {
        FillMode.Mandated => "by the " + culture.RulerTitle.ToLowerInvariant() + "'s mandate",
        FillMode.Customary => Offices.CustomaryClaim,
        _ => kind switch
        {
            OfficeKind.Governor => "by the town's own council",
            OfficeKind.HighPriest => "by the acclamation of the faithful",
            _ => "by the choice of the realm",
        },
    };

    // -----------------------------------------------------------------------
    // Disgrace
    // -----------------------------------------------------------------------

    /// <summary>
    /// Strips an office from someone it has gone badly for.
    /// </summary>
    /// <remarks>
    /// A cause the model actually has, rather than a roll dressed as one: a marshal is dismissed
    /// while the realm is bleeding, and a governor while the town they hold is emptying. Both are
    /// scaled by how aggressive the realm currently is, since a patient court reassigns and an
    /// impatient one makes an example.
    /// </remarks>
    private static void Disgrace(
        WorldState world, Civilization civilization, int year, IRng court)
    {
        double temper = world.ValuesFor(civilization).Aggression;

        foreach (Figure figure in world.Figures)
        {
            OfficeHolding? held = figure.CurrentOffice;
            if (held is null || held.CivilizationId != civilization.Id || !figure.IsAlive) continue;

            string? failure = FailureOf(world, civilization, held);
            if (failure is null) continue;

            IRng fate = court.Fork("disgrace", figure.Id.ToDiscriminator());
            if (!fate.Chance(DisgraceChance * DetMath.Lerp(0.5, 1.5, temper))) continue;

            Offices.Revoke(world, figure, held.Kind, failure, year);
        }
    }

    private static string? FailureOf(
        WorldState world, Civilization civilization, OfficeHolding held)
    {
        if (held.Kind == OfficeKind.Marshal && civilization.Fortunes.Weariness > 0.5)
        {
            return "the war having gone against the realm";
        }

        if (held.Kind == OfficeKind.Governor
            && world.Settlements.Contains(held.ScopeId)
            && world.Settlements[held.ScopeId].YearsDepressed > 10)
        {
            return "the town having emptied under their charge";
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Lookups
    // -----------------------------------------------------------------------

    private static bool AtWar(WorldState world, EntityId civilizationId)
    {
        foreach (War war in world.ActiveWars())
        {
            if (war.Involves(civilizationId)) return true;
        }

        return false;
    }

    /// <summary>Whoever the appointments are made by: the regent while one sits, else the ruler.</summary>
    private static Figure? Governing(WorldState world, Civilization civilization)
    {
        if (world.Figures.Contains(civilization.RegentId))
        {
            Figure regent = world.Figures[civilization.RegentId];
            if (regent.IsAlive) return regent;
        }

        if (world.Figures.Contains(civilization.CurrentRulerId))
        {
            Figure ruler = world.Figures[civilization.CurrentRulerId];
            if (ruler.IsAlive) return ruler;
        }

        return null;
    }

    private static bool Held(WorldState world, Civilization civilization, OfficeKind kind)
    {
        foreach (Figure figure in world.Figures)
        {
            OfficeHolding? held = figure.OpenOffice(kind);
            if (held is not null && held.CivilizationId == civilization.Id && figure.IsAlive)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The sitting governor of a settlement, if it has one.</summary>
    public static Figure? GovernorOf(WorldState world, Settlement settlement) =>
        Offices.GovernorOf(world, settlement);
}
