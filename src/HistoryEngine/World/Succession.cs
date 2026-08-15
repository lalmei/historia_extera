using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// Who stands where in a line of succession, read off the family tree.
/// </summary>
/// <remarks>
/// <para>The one piece of Milestone 5 worth writing carefully. Everything else — marriages,
/// births, regencies — is bookkeeping around this traversal, and the difference between a
/// chronicle that reads like a dynasty and one that reads like a list of names is entirely
/// whether the next ruler is found <em>here</em> or invented.</para>
///
/// <para><b>The traversal.</b> Starting from the last ruler, walk their descendants depth-first,
/// eldest line exhausted before the next child is considered — so a king's grandson by his
/// eldest son outranks his own second son, which is what primogeniture actually means and what
/// a sorted list of relatives gets wrong. Only when that line is exhausted does the walk climb
/// to the ruler's parent and descend again, picking up siblings and their lines, then to the
/// grandparent for uncles and cousins, and so on to the founder.</para>
///
/// <para><b>Climb through the house, descend through anyone.</b> The upward climb stops at the
/// first ancestor who is not blood of the ruling house, because a claim originates in a house.
/// The downward walk is not house-bounded, because a daughter's children belong to their
/// father's house and still inherit under every law but the agnatic one — which is precisely how
/// a crown passes from one house to another without anybody dying out. Making the descent
/// house-bounded is the tempting simplification, and it silently converts every such succession
/// into an extinction.</para>
///
/// <para>Two consumers, one traversal: <see cref="Claimants"/> answers who may take the throne,
/// and <see cref="Kin"/> answers who is close enough to it for the chronicle to keep following.
/// They differ only in their filter, which is deliberate — a house's marriage roster and its
/// line of succession are the same people seen through two questions.</para>
/// </remarks>
public static class Succession
{
    /// <summary>Age at which a ruler governs in their own right rather than under a regent.</summary>
    public const int MajorityAge = 16;

    /// <summary>How many of the strongest claims an elective government actually chooses between.</summary>
    public const int ElectorateSize = 4;

    /// <summary>Relative weight of each place on an elective ballot, strongest claim first.</summary>
    public static readonly IReadOnlyList<double> BallotWeights = new[] { 0.45, 0.27, 0.17, 0.11 };

    /// <summary>
    /// The narrowest and widest the electorate's own preference may move a claim.
    /// </summary>
    /// <remarks>
    /// <para><b>Claim strength must stay the dominant term.</b> This is the sharpest calibration
    /// risk in the reign-aware layer: widen this range and an elective realm stops being dynastic
    /// altogether — the ballot becomes pure trait-matching, the fourth-placed claimant wins
    /// routinely, and the whole apparatus of houses and lines of descent stops mattering in
    /// exactly the governments that were meant to show it off.</para>
    ///
    /// <para>The invariant that keeps it honest, and which <c>DispositionTests</c> asserts: the
    /// least-wanted first claimant must still outweigh the most-wanted last one.</para>
    /// </remarks>
    public const double MinFavour = 0.5;

    public const double MaxFavour = 1.6;

    /// <summary>Distance at or below which a candidate is exactly what the realm was hoping for.</summary>
    private const double CloseEnough = 0.15;

    /// <summary>Distance at or beyond which they are not what it wanted at all.</summary>
    private const double FarEnough = 0.45;

    /// <summary>How devout a fervent establishment wants its ruler.</summary>
    private const double FaithWantsPiety = 0.5;

    /// <summary>How much a realm in trouble wants a firm hand rather than a delegating one.</summary>
    private const double CrisisWantsAStrongHand = 0.4;

    /// <summary>
    /// The ruler this realm would choose if it could describe one.
    /// </summary>
    /// <remarks>
    /// <para>Derived and stored nowhere. It is the realm's own culture as its recent past leaves
    /// it — the same shifts a reign is judged through, applied to what a people asks for rather
    /// than to what it does — plus two things that bear on a ruler specifically: an establishment
    /// with a fervent faith wants a devout one, and a realm in crisis wants a firm hand.</para>
    ///
    /// <para>Read by elective ballots and by disputed successions, so "the realm backed the
    /// brother who promised war" is available under primogeniture too, without a second system and
    /// without anyone casting a ballot in a kingdom.</para>
    /// </remarks>
    public static Disposition Wanted(WorldState world, Civilization civilization, Culture culture)
    {
        CultureValues values = culture.Values.ShiftedBy(civilization.Fortunes);

        EntityId faithId = world.FaithOf(civilization);
        if (world.Religions.Contains(faithId))
        {
            double fervour = world.Religions[faithId].Fervour;
            values = values with
            {
                Piety = DetMath.Lerp(values.Piety, 1.0, fervour * FaithWantsPiety),
            };
        }

        // Weariness and calamity both argue for someone who will simply decide. Grievance does
        // not: a realm wanting its province back wants a fighter, which the values above already
        // say, not a firmer hand at home.
        double crisis = (civilization.Fortunes.Weariness + civilization.Fortunes.Calamity) * 0.5;

        double centralism = DetMath.Clamp01(
            Disposition.CentralismNorm(culture.Government)
            + (crisis * CrisisWantsAStrongHand));

        return new Disposition(values, centralism);
    }

    /// <summary>
    /// How much more, or less, than their claim alone this candidate is wanted.
    /// </summary>
    /// <remarks>
    /// A multiplier on a ballot weight rather than a term added to it, so it scales a claim
    /// instead of replacing one. Centralism counts as one dial among the seven, not as a seventh
    /// of the answer on its own.
    /// </remarks>
    public static double Favour(Figure candidate, Disposition wanted)
    {
        double distance =
            ((candidate.Disposition.Values.DistanceTo(wanted.Values) * 6.0)
             + Math.Abs(candidate.Disposition.Centralism - wanted.Centralism))
            / 7.0;

        double match = DetMath.Clamp01(
            DetMath.InverseLerp(FarEnough, CloseEnough, distance));

        return DetMath.Lerp(MinFavour, MaxFavour, match);
    }

    /// <summary>
    /// Everyone entitled to take a civilization's throne, strongest claim first.
    /// </summary>
    /// <param name="exclude">
    /// The outgoing ruler. Excluded rather than filtered by liveness because a term ending leaves
    /// them alive and standing at the front of their own line, which would re-elect them forever.
    /// </param>
    public static List<Figure> Claimants(
        WorldState world, Civilization civilization, Culture culture, EntityId exclude)
    {
        SuccessionLaw law = culture.Succession;
        Dynasty? house = HouseOf(world, civilization);
        if (house is null) return new List<Figure>();

        bool Qualifies(Figure figure)
        {
            if (!figure.IsAlive || figure.Id == exclude) return false;
            if (HoldsAThrone(world, figure)) return false;
            if (law == SuccessionLaw.Agnatic && figure.Sex != Sex.Male) return false;

            // An elected office goes to someone who can hold it. A hereditary one can pass to a
            // child, which is what regencies exist for.
            return law != SuccessionLaw.Elective || figure.AgeIn(world.Year) >= MajorityAge;
        }

        if (law == SuccessionLaw.Seniority)
        {
            return Eldest(world, house, Qualifies);
        }

        List<Figure> line = Walk(
            world,
            house,
            Reference(world, civilization, house),
            law,
            includeStart: false,
            Qualifies,
            new bool[world.Figures.Count]);

        if (law == SuccessionLaw.Elective)
        {
            AppendResidentDynasts(world, civilization, line, Qualifies);

            // One candidate per house, each house's strongest. An election is a choice between
            // families, and without this it is not one: the sitting house's own line is listed
            // first and fills the whole ballot, so it simply keeps electing itself — one house
            // held the consulship of a republic for thirty-six consecutive terms before this,
            // which is an elective realm in name and a hereditary one in every other respect.
            line = OnePerHouse(line);

            // A realm is wary of a family that already rules its neighbours. Without this an
            // elective world consolidates: the largest house has the most candidates everywhere,
            // wins everywhere, and eight centuries end with two families holding every throne and
            // every other house extinct. Historically this is why elective monarchies balanced
            // against dominant houses, and it is the only thing that keeps them from converging.
            line = Prefer(line, candidate => !RulesElsewhere(world, candidate, civilization));

            // An office with a term comes round often enough that a realm with two eligible
            // dynasts will otherwise hand it back and forth between the same pair for a century.
            // Passing over anyone who has already held it spends the chronicle on a succession of
            // names rather than on an alternation of two.
            line = Prefer(line, candidate => !civilization.RulerIds.Contains(candidate.Id));
        }

        return line;
    }

    /// <summary>Keeps each house's strongest claimant and drops the rest of its line.</summary>
    private static List<Figure> OnePerHouse(List<Figure> line)
    {
        var standing = new List<Figure>(line.Count);
        var houses = new List<EntityId>(line.Count);

        foreach (Figure candidate in line)
        {
            // Someone of no house stands for themselves, and never crowds out another.
            if (!candidate.DynastyId.IsNone)
            {
                if (houses.Contains(candidate.DynastyId)) continue;
                houses.Add(candidate.DynastyId);
            }

            standing.Add(candidate);
        }

        return standing;
    }

    /// <summary>
    /// Narrows a ballot to the candidates a realm would rather have, unless that leaves nobody.
    /// </summary>
    /// <remarks>
    /// A preference rather than a rule: every one of these would, applied absolutely, eventually
    /// hand a realm no candidate at all and send a perfectly healthy house to the fallback that
    /// invents a stranger.
    /// </remarks>
    private static List<Figure> Prefer(List<Figure> line, Func<Figure, bool> preferred)
    {
        var kept = new List<Figure>(line.Count);

        foreach (Figure candidate in line)
        {
            if (preferred(candidate)) kept.Add(candidate);
        }

        return kept.Count > 0 ? kept : line;
    }

    /// <summary>Whether a candidate's house already holds some other realm's throne.</summary>
    public static bool RulesElsewhere(WorldState world, Figure candidate, Civilization here)
    {
        if (candidate.DynastyId.IsNone) return false;

        foreach (Civilization other in world.ActiveCivilizations())
        {
            if (other.Id == here.Id) continue;
            if (other.RulingDynastyId == candidate.DynastyId) return true;
        }

        return false;
    }

    /// <summary>
    /// The living members of a house, ordered by how near the throne they stand.
    /// </summary>
    /// <remarks>
    /// The chronicle's attention budget. Marriages are arranged and children are born for those
    /// near the front of this list and for nobody else — not as a claim about biology but as one
    /// about the record: a chronicle follows the throne, and a house's remote cousins fall out of
    /// it. It is also what keeps the figure table from growing exponentially, since a branch that
    /// drifts far enough from the succession simply stops being written down.
    /// </remarks>
    public static List<Figure> Kin(WorldState world, Civilization civilization)
    {
        Dynasty? house = HouseOf(world, civilization);
        if (house is null) return new List<Figure>();

        Figure? start = world.Figures.Contains(civilization.CurrentRulerId)
            ? world.Figures[civilization.CurrentRulerId]
            : Reference(world, civilization, house);

        return Kin(world, house, start);
    }

    /// <summary>
    /// The same roster for a house holding no throne, measured from whoever last held one.
    /// </summary>
    /// <remarks>
    /// A house out of power still has a head and still has heirs, and a chronicle in which losing
    /// the throne meant a family stopped having children would never see one return to it — which
    /// is most of what makes elective politics worth simulating.
    /// </remarks>
    public static List<Figure> Kin(WorldState world, Dynasty house)
    {
        Figure? head = house.RulerIds.Count > 0
            ? world.Figures[house.RulerIds[house.RulerIds.Count - 1]]
            : world.Figures.Contains(house.FounderId) ? world.Figures[house.FounderId] : null;

        return Kin(world, house, head);
    }

    private static List<Figure> Kin(WorldState world, Dynasty house, Figure? head)
    {
        static bool Alive(Figure figure) => figure.IsAlive;

        var seen = new bool[world.Figures.Count];
        List<Figure> kin = Walk(
            world, house, head, SuccessionLaw.Absolute, includeStart: true, Alive, seen);

        // A member the descent could not reach — someone whose only living link runs through a
        // branch the walk already closed — is still of the house, and still marriageable.
        foreach (EntityId id in house.MemberIds)
        {
            if (seen[id.Index]) continue;

            Figure member = world.Figures[id];
            if (member.IsAlive) kin.Add(member);
        }

        return kin;
    }

    /// <summary>Whether two figures are near enough in blood that a marriage between them is barred.</summary>
    /// <remarks>
    /// Siblings, half-siblings, parent and child, and grandparent and grandchild. Cousins are
    /// permitted, as they historically were and as dynastic politics rather required.
    /// </remarks>
    public static bool AreCloseKin(WorldState world, Figure a, Figure b)
    {
        if (a.Id == b.Id) return true;
        if (SharesAParent(a, b)) return true;
        return IsWithinTwoGenerations(world, a, b) || IsWithinTwoGenerations(world, b, a);
    }

    /// <summary>The house whose claim a civilization's throne currently answers to.</summary>
    public static Dynasty? HouseOf(WorldState world, Civilization civilization) =>
        world.Dynasties.Contains(civilization.RulingDynastyId)
            ? world.Dynasties[civilization.RulingDynastyId]
            : null;

    /// <summary>Whether this figure already sits on a throne. Nobody holds two at once.</summary>
    /// <remarks>
    /// A personal union is a genuinely interesting outcome and belongs to Milestone 6's
    /// diplomacy, not to a succession rule that would produce it by accident.
    /// </remarks>
    public static bool HoldsAThrone(WorldState world, Figure figure)
    {
        if (!world.Civilizations.Contains(figure.CivilizationId)) return false;
        return world.Civilizations[figure.CivilizationId].CurrentRulerId == figure.Id;
    }

    /// <summary>
    /// The figure the line is measured from: the last person to hold the throne, else the founder.
    /// </summary>
    private static Figure? Reference(WorldState world, Civilization civilization, Dynasty house)
    {
        for (int i = civilization.RulerIds.Count - 1; i >= 0; i--)
        {
            Figure past = world.Figures[civilization.RulerIds[i]];
            if (past.DynastyId == house.Id) return past;
        }

        return world.Figures.Contains(house.FounderId) ? world.Figures[house.FounderId] : null;
    }

    /// <summary>
    /// Descend, climb, descend again — until the claim runs out of house to climb through.
    /// </summary>
    /// <param name="seen">
    /// Visited marks, indexed by figure. A flat array rather than a list because the walk revisits
    /// the same subtree once per ancestor it climbs past, and a linear membership scan turns a
    /// house of two hundred into forty thousand comparisons every year of the run.
    /// </param>
    private static List<Figure> Walk(
        WorldState world,
        Dynasty house,
        Figure? start,
        SuccessionLaw law,
        bool includeStart,
        Func<Figure, bool> qualifies,
        bool[] seen)
    {
        var ordered = new List<Figure>();

        Figure? anchor = start;
        bool include = includeStart;

        while (anchor is not null)
        {
            Descend(world, anchor, law, include, qualifies, ordered, seen);

            anchor = ClaimParent(world, anchor, house.Id);
            include = true;
        }

        return ordered;
    }

    private static void Descend(
        WorldState world,
        Figure figure,
        SuccessionLaw law,
        bool include,
        Func<Figure, bool> qualifies,
        List<Figure> ordered,
        bool[] seen)
    {
        if (seen[figure.Id.Index]) return;
        seen[figure.Id.Index] = true;

        if (include && qualifies(figure)) ordered.Add(figure);

        foreach (Figure child in Children(world, figure, law))
        {
            Descend(world, child, law, include: true, qualifies, ordered, seen);
        }
    }

    /// <summary>A figure's children in the order the law considers them.</summary>
    private static List<Figure> Children(WorldState world, Figure figure, SuccessionLaw law)
    {
        var children = new List<Figure>(figure.ChildIds.Count);

        foreach (EntityId id in figure.ChildIds)
        {
            Figure child = world.Figures[id];

            // Under agnatic law a daughter neither inherits nor transmits, so her line is not
            // walked at all — the distinction between "cannot inherit" and "cannot pass on a
            // claim" is the whole difference between agnatic and male-preference succession.
            if (law == SuccessionLaw.Agnatic && child.Sex != Sex.Male) continue;

            children.Add(child);
        }

        bool malesFirst = law is SuccessionLaw.MalePreference or SuccessionLaw.Agnatic;

        // Id breaks every tie, because List.Sort is unstable and two children born in the same
        // year would otherwise order unpredictably between runs.
        children.Sort((a, b) =>
        {
            if (malesFirst && a.Sex != b.Sex) return a.Sex == Sex.Male ? -1 : 1;

            int byBirth = a.BirthYear.CompareTo(b.BirthYear);
            return byBirth != 0 ? byBirth : a.Id.CompareTo(b.Id);
        });

        return children;
    }

    /// <summary>
    /// The parent a claim climbs through: one who is blood of the house, or nothing.
    /// </summary>
    /// <remarks>
    /// Stopping at the house boundary is what keeps the climb finite and honest. Without it the
    /// walk would wander up into a consort's ancestry and start offering an unrelated house's
    /// cousins a claim to this throne.
    /// </remarks>
    private static Figure? ClaimParent(WorldState world, Figure figure, EntityId houseId)
    {
        foreach (EntityId id in figure.Parents())
        {
            Figure parent = world.Figures[id];
            if (parent.DynastyId == houseId) return parent;
        }

        return null;
    }

    /// <summary>Every qualifying member of the house, eldest first. Seniority in one line.</summary>
    private static List<Figure> Eldest(WorldState world, Dynasty house, Func<Figure, bool> qualifies)
    {
        var line = new List<Figure>();

        foreach (EntityId id in house.MemberIds)
        {
            Figure member = world.Figures[id];
            if (qualifies(member)) line.Add(member);
        }

        line.Sort((a, b) =>
        {
            int byAge = a.BirthYear.CompareTo(b.BirthYear);
            return byAge != 0 ? byAge : a.Id.CompareTo(b.Id);
        });

        return line;
    }

    /// <summary>
    /// Adds dynasts of other houses living in the realm to an elective ballot.
    /// </summary>
    /// <remarks>
    /// What makes an elected office genuinely different from an inherited one: over time a realm
    /// accumulates the houses that have married into it, and the crown can pass sideways between
    /// them without anyone dying out.
    /// </remarks>
    private static void AppendResidentDynasts(
        WorldState world, Civilization civilization, List<Figure> line, Func<Figure, bool> qualifies)
    {
        foreach (Dynasty house in world.Dynasties)
        {
            if (house.Id == civilization.RulingDynastyId) continue;

            foreach (EntityId id in house.MemberIds)
            {
                Figure member = world.Figures[id];

                if (member.CivilizationId != civilization.Id) continue;
                if (!qualifies(member) || Holds(line, member.Id)) continue;

                line.Add(member);
            }
        }
    }

    private static bool SharesAParent(Figure a, Figure b)
    {
        foreach (EntityId parent in a.Parents())
        {
            foreach (EntityId other in b.Parents())
            {
                if (parent == other) return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="descendant"/> is a child or grandchild of <paramref name="elder"/>.</summary>
    private static bool IsWithinTwoGenerations(WorldState world, Figure elder, Figure descendant)
    {
        foreach (EntityId parentId in descendant.Parents())
        {
            if (parentId == elder.Id) return true;

            Figure parent = world.Figures[parentId];
            foreach (EntityId grandparentId in parent.Parents())
            {
                if (grandparentId == elder.Id) return true;
            }
        }

        return false;
    }

    private static bool Holds(List<Figure> figures, EntityId id)
    {
        for (int i = 0; i < figures.Count; i++)
        {
            if (figures[i].Id == id) return true;
        }

        return false;
    }
}
