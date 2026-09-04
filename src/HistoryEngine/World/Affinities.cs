using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// Friendships between two named people, from the contact that allowed one to how it ended.
/// </summary>
/// <remarks>
/// <para><b>The affiliative half of a social graph that only had the other one.</b> Every durable
/// tie the engine could write was either a fact of birth, a fact of office, or hostility:
/// <see cref="BondKind.Friend"/> was declared with the rest and had no write site anywhere, so the
/// only way two people could end up close was to be born related, appoint each other, or stand in
/// the same battle line. An ordinary peacetime life therefore had nothing in it, which is not a
/// sparse life honestly recorded — it is a category of experience the model could not represent.
/// </para>
///
/// <para><b>Contact is a precondition, not a roll.</b> Nothing here surveys a realm for two people
/// who might get on. A pair becomes eligible only by sharing a town in the year in question, and
/// what makes them likely is what the record already holds about them: a warm bond of office or
/// comradeship, a career in common, an experience in common, dispositions that are not opposed.
/// Sharing a realm is not contact — that is the mistake <see cref="Disputes"/> refused for
/// quarrels, and it produces a great deal of friendship that means nothing.</para>
///
/// <para><b>The ladder is what has been risked.</b> Known to each other, then a good turn done,
/// then a confidence given, then a tie both would name. No rung may be skipped and at most one is
/// walked per year, so a friendship in the export always shows the years it took to become one.
/// A friendship at the top rung is not closed: it stands, is reinforced for as long as the two
/// remain within reach of each other, and cools if they do not.</para>
///
/// <para><b>A betrayal needs something to betray.</b> It is reachable only from the two upper rungs
/// and only where the world has already written a reason — a grievance recorded against the friend,
/// an open quarrel between them, or a plot the betrayer is running against the friend's life. Where
/// there is no such pull nobody turns; there is no annual chance that a friend goes bad. This is
/// the one thing on the ladder a chronicle keeps, and it is what the rest of the ladder is for.</para>
///
/// <para>Deaths close friendships through <see cref="EndAtDeath"/>, called from
/// <see cref="Houses.Die"/> so the export can never show a standing friendship with a dead man in
/// it. Nothing here kills anybody: a betrayal leaves enmity, and what enmity becomes is already
/// answered by <see cref="Disputes"/> and <see cref="Conspiracies"/>.</para>
/// </remarks>
public static class Affinities
{
    /// <summary>Years without an act after which a friendship is treated as having cooled.</summary>
    private const int StaleYears = 12;

    /// <summary>
    /// How many friendships one person may be carrying at once.
    /// </summary>
    /// <remarks>
    /// Low, because the list is meant to be the people who matter rather than everyone a courtier
    /// was ever civil to. It is also the brake on the whole system: the pair loop below is over
    /// co-residents, so without a cap a capital would produce a complete graph of its court.
    /// </remarks>
    private const int OpenCapacity = 3;

    /// <summary>The floor a recorded grievance must clear before a friend might turn on the other.</summary>
    private const double BetrayalFloor = 0.30;

    /// <summary>Chance a co-resident pair with nothing else in common come to know each other.</summary>
    private const double ContactFloor = 0.006;

    private const double ContactFromWarmth = 0.055;

    // -----------------------------------------------------------------------
    // The yearly pass
    // -----------------------------------------------------------------------

    /// <summary>Carries every standing friendship one year further, and lets new ones begin.</summary>
    /// <remarks>
    /// Standing ones first. A pair whose friendship ended this year is then not also a pair that
    /// struck one up in the same year, and the capacity a cooling frees is available immediately
    /// rather than a year later — which is the behaviour a reader would assume from the dates.
    /// </remarks>
    public static void Tick(WorldState world, int year)
    {
        Advance(world, year);
        Form(world, year);
    }

    private static void Advance(WorldState world, int year)
    {
        // Gathered before anything is advanced, for the reason the quarrel pass gathers: an ending
        // reaches LifeStories and from there into lists this loop would otherwise be standing in.
        var open = new List<FigureAffinity>();
        foreach (Figure figure in world.Figures)
        {
            foreach (FigureAffinity affinity in figure.Affinities)
            {
                if (!affinity.IsOpen || affinity.OpenerId != figure.Id) continue;
                if (!world.Figures.Contains(affinity.FriendId)) continue;

                open.Add(affinity);
            }
        }

        foreach (FigureAffinity affinity in open)
        {
            if (!affinity.IsOpen) continue;

            Carry(
                world,
                world.Figures[affinity.OpenerId],
                world.Figures[affinity.FriendId],
                affinity,
                year);
        }
    }

    private static void Carry(
        WorldState world, Figure opener, Figure friend, FigureAffinity affinity, int year)
    {
        if (!opener.IsAlive || !friend.IsAlive)
        {
            Close(
                affinity,
                year,
                AffinityOutcome.Lapsed,
                EventKind.FigureDied,
                "death ended it");
            return;
        }

        if (!InReach(world, opener, friend))
        {
            End(
                world,
                affinity,
                opener,
                friend,
                year,
                AffinityOutcome.Parted,
                "a border came between them");
            return;
        }

        bool together = world.ResidenceOf(opener) == world.ResidenceOf(friend);

        if (Turns(world, opener, friend, affinity, year)) return;

        if (year - affinity.LastActionYear >= StaleYears)
        {
            End(
                world,
                affinity,
                opener,
                friend,
                year,
                AffinityOutcome.Cooled,
                "it went cold with the years");
            return;
        }

        if (affinity.Stage == AffinityStage.Friendship)
        {
            // A standing friendship does not climb, and there is nothing to write about a year in
            // which two friends went on being friends. Living in the same town is what keeps the
            // stale clock from reaching it; friends who end up in different towns drift, which is
            // the one thing residence history was always able to say and nothing yet asked it.
            if (!together) return;

            affinity.LastActionYear = year;
            LifeStories.Warm(
                opener, friend, year, EventKind.AffinityDeepened, world.ResidenceOf(opener));
            return;
        }

        if (!together) return;

        double warmth = Warmth(world, opener, friend, year);
        double climb = DetMath.Clamp(0.06 + (0.34 * warmth), 0.02, 0.55);
        if (!Fork(world, opener, friend, year, "deepen").Chance(climb)) return;

        Deepen(world, opener, friend, affinity, year);
    }

    /// <summary>Walks one rung, and writes what was done to get there.</summary>
    private static void Deepen(
        WorldState world, Figure opener, Figure friend, FigureAffinity affinity, int year)
    {
        affinity.Stage++;
        affinity.LastActionYear = year;
        affinity.PlaceId = world.ResidenceOf(opener);

        // A favour has a giver and a receiver; the other two rungs are mutual and are recorded
        // against the person who sought the friendship in the first place. Standing decides who
        // gives, because a good turn is mostly the ability to do one.
        Figure actor = affinity.Stage == AffinityStage.Kindness
            ? Means(opener, year) >= Means(friend, year) ? opener : friend
            : opener;
        Figure other = actor.Id == opener.Id ? friend : opener;

        affinity.Acts.Add(new AffinityAct(
            year,
            EventKind.AffinityDeepened,
            affinity.Stage,
            actor.Id,
            StageDetail(affinity.Stage)));

        switch (affinity.Stage)
        {
            case AffinityStage.Kindness:
                LifeStories.AddFavour(
                    actor, other, year, EventKind.AffinityDeepened, affinity.PlaceId);
                break;
            case AffinityStage.Confidence:
                LifeStories.AddConfidence(
                    opener, friend, year, EventKind.AffinityDeepened, affinity.PlaceId);
                break;
            default:
                LifeStories.AddFriendship(
                    opener, friend, year, EventKind.AffinityDeepened, affinity.PlaceId);
                break;
        }

        world.Chronicle.Record(
            year,
            EventKind.AffinityDeepened,
            actor.Id,
            obj: other.Id,
            location: affinity.PlaceId,
            data: Chronicle.Data(
                ("act", StageVerb(affinity.Stage)),
                ("actSelf", StageVerbCapitalised(affinity.Stage))),
            significance: Significance.Routine);
    }

    // -----------------------------------------------------------------------
    // Beginning
    // -----------------------------------------------------------------------

    /// <summary>
    /// Lets people who shared a town this year come to know each other.
    /// </summary>
    /// <remarks>
    /// <para>Bucketed by resolved residence in one pass, then evaluated pair by pair. Each pair's
    /// chance is forked from the two people and the year rather than drawn from a stream the loop
    /// shares, so whether two people meet does not depend on who else was in the room — the
    /// property the quarrel model gets for free by never surveying, and the closest this one can
    /// come to it while still asking a question about proximity.</para>
    ///
    /// <para>The capacity check is the exception and is deliberately live: whether a person has room
    /// for another friend genuinely depends on the friendships they already have, and one of those
    /// may have been made earlier in this same pass. It is the one place here where iteration order
    /// can decide something, and it decides which of two possible friendships happened rather than
    /// whether either could.</para>
    /// </remarks>
    private static void Form(WorldState world, int year)
    {
        var buckets = new DetMap<EntityId, List<Figure>>();

        foreach (Figure figure in world.Figures)
        {
            if (!Eligible(world, figure, year)) continue;

            EntityId home = world.ResidenceOf(figure);
            if (home.IsNone) continue;

            if (!buckets.TryGetValue(home, out List<Figure>? residents) || residents is null)
            {
                residents = new List<Figure>();
                buckets[home] = residents;
            }

            residents.Add(figure);
        }

        foreach ((EntityId home, List<Figure> residents) in buckets)
        {
            for (int i = 0; i < residents.Count; i++)
            {
                for (int j = i + 1; j < residents.Count; j++)
                {
                    Consider(world, residents[i], residents[j], home, year);
                }
            }
        }
    }

    private static void Consider(
        WorldState world, Figure first, Figure second, EntityId home, int year)
    {
        if (!Available(first) || !Available(second)) return;
        if (Known(first, second)) return;

        FigureBond? bond = LifeStories.BondTo(first, second.Id);
        if (bond is not null)
        {
            // Close family is already the closest tie the engine records, and a quarrel is the
            // wrong place to start a friendship from — an enmity that warms is a reconciliation,
            // which the quarrel model already owns and answers on its own terms.
            BondKind barred = BondKind.Kin | BondKind.Spouse | BondKind.Rival | BondKind.Enemy;
            if ((bond.Kinds & barred) != BondKind.None) return;
        }

        double warmth = Warmth(world, first, second, year);
        double chance = DetMath.Clamp(
            ContactFloor + (ContactFromWarmth * warmth), 0.0, 0.20);
        if (!Fork(world, first, second, year, "meet").Chance(chance)) return;

        AffinityOrigin origin = Origin(bond);
        var affinity = new FigureAffinity(
            OpenedBy(first),
            first.Id,
            second.Id,
            year,
            origin,
            SourceOf(origin),
            second.Id,
            home);

        affinity.Acts.Add(new AffinityAct(
            year,
            EventKind.AcquaintanceFormed,
            AffinityStage.Acquaintance,
            first.Id,
            OriginDetail(origin)));

        first.Affinities.Add(affinity);
        second.Affinities.Add(affinity);

        LifeStories.AddAcquaintance(first, second, year, EventKind.AcquaintanceFormed, home);

        world.Chronicle.Record(
            year,
            EventKind.AcquaintanceFormed,
            first.Id,
            obj: second.Id,
            location: home,
            data: Chronicle.Data(("cause", OriginDetail(origin))),
            significance: Significance.Routine);
    }

    // -----------------------------------------------------------------------
    // Turning
    // -----------------------------------------------------------------------

    /// <summary>
    /// Offers one friend the chance to turn on the other, where the world gave them a reason to.
    /// </summary>
    /// <remarks>
    /// Both sides are asked, in id order, and at most one turns. The gate is the whole design: a
    /// friend with no recorded grievance, no open quarrel and no plot against the other's life
    /// cannot betray them at any odds, so a betrayal in the export is always answerable with the
    /// year and the entity that produced it.
    /// </remarks>
    private static bool Turns(
        WorldState world, Figure opener, Figure friend, FigureAffinity affinity, int year)
    {
        if (affinity.Stage < AffinityStage.Confidence) return false;

        return Turn(world, opener, friend, affinity, year)
            || Turn(world, friend, opener, affinity, year);
    }

    private static bool Turn(
        WorldState world, Figure betrayer, Figure betrayed, FigureAffinity affinity, int year)
    {
        FigureBond? bond = LifeStories.BondTo(betrayer, betrayed.Id);
        double grievance = bond?.Grievance ?? 0.0;
        bool quarrelling = betrayer.Disputes.Exists(
            dispute => dispute.IsOpen && dispute.Involves(betrayed.Id));
        bool plotting = betrayer.Plots.Exists(
            plot => plot.IsOpen && plot.TargetId == betrayed.Id);

        if (grievance < BetrayalFloor && !quarrelling && !plotting) return false;

        double pull = (grievance * 0.42)
            + (quarrelling ? 0.20 : 0.0)
            + (plotting ? 0.34 : 0.0)
            + (betrayer.Disposition.Values.Aggression * 0.14);

        // What the friendship itself is worth is what holds a person back, and it is read from the
        // bond rather than from the rung: a friendship carried on obligation survives a grievance
        // that one carried on nothing would not.
        double hold = bond is null
            ? 0.0
            : (bond.Obligation * 0.40)
                + (Math.Max(0.0, bond.Affection) * 0.30)
                + (Math.Max(0.0, bond.Trust) * 0.20);

        double chance = DetMath.Clamp(
            (0.08 + (0.46 * DetMath.Clamp01(pull))) * (1.0 - DetMath.Clamp01(hold)),
            0.01,
            0.50);
        if (!Fork(world, betrayer, betrayed, year, "turn").Chance(chance)) return false;

        EntityId place = world.ResidenceOf(betrayed);
        affinity.BetrayerId = betrayer.Id;
        affinity.PlaceId = place;
        Close(
            affinity,
            year,
            AffinityOutcome.Betrayed,
            EventKind.FriendshipBetrayed,
            "one of them turned on the other",
            act: "turned on them",
            actor: betrayer.Id);

        LifeStories.Betray(
            betrayer,
            betrayed,
            year,
            EventKind.FriendshipBetrayed,
            place,
            DetMath.Clamp01(0.45 + (0.55 * DetMath.Clamp01(pull))));

        world.Chronicle.Record(
            year,
            EventKind.FriendshipBetrayed,
            betrayer.Id,
            obj: betrayed.Id,
            location: place,
            data: Chronicle.Data(("cause", TurnDetail(quarrelling, plotting))));

        return true;
    }

    // -----------------------------------------------------------------------
    // Endings
    // -----------------------------------------------------------------------

    /// <summary>
    /// Closes every friendship a person's death leaves without a second party.
    /// </summary>
    /// <remarks>
    /// Called from the death itself rather than left to next year's pass, so that no export can
    /// show a standing friendship with a dead man in it — including the case that would otherwise
    /// always be caught, a death in the final year of a run.
    /// </remarks>
    public static void EndAtDeath(WorldState world, Figure figure, int year)
    {
        foreach (FigureAffinity affinity in figure.Affinities)
        {
            if (!affinity.IsOpen) continue;

            Close(
                affinity,
                year,
                AffinityOutcome.Lapsed,
                EventKind.FigureDied,
                "death ended it");
        }
    }

    /// <summary>An ending the two of them were both alive for, which is the only kind worth a line.</summary>
    private static void End(
        WorldState world,
        FigureAffinity affinity,
        Figure opener,
        Figure friend,
        int year,
        AffinityOutcome outcome,
        string how)
    {
        Close(affinity, year, outcome, EventKind.AffinityEnded, how);

        // Only friendships that got as far as being one are worth telling anybody about. An
        // acquaintance that came to nothing coming to nothing is not an event, and the timeline
        // would carry a great many of them.
        if (affinity.Stage < AffinityStage.Friendship) return;

        world.Chronicle.Record(
            year,
            EventKind.AffinityEnded,
            opener.Id,
            obj: friend.Id,
            location: affinity.PlaceId,
            data: Chronicle.Data(("manner", how)),
            significance: Significance.Routine);
    }

    /// <summary>Writes the ending into the shared record both parties read.</summary>
    /// <remarks>
    /// The act and the resolution are the same fact in two grammars, as they are for a quarrel: one
    /// completes a list of things that were done, the other completes "ended when".
    /// </remarks>
    private static void Close(
        FigureAffinity affinity,
        int year,
        AffinityOutcome outcome,
        EventKind actKind,
        string how,
        string? act = null,
        EntityId actor = default)
    {
        affinity.Outcome = outcome;
        affinity.Resolution = how;
        affinity.EndYear = year;
        affinity.LastActionYear = year;
        affinity.Acts.Add(new AffinityAct(
            year,
            actKind,
            affinity.Stage,
            actor.IsNone ? affinity.OpenerId : actor,
            act ?? how));
    }

    // -----------------------------------------------------------------------
    // Reading the world
    // -----------------------------------------------------------------------

    /// <summary>
    /// How likely these two are to get on, from what the record already holds about them.
    /// </summary>
    /// <remarks>
    /// Every term is something the world wrote down: a bond of office or comradeship, a career in
    /// common, an experience in common, a distance in age, a distance in rank. None of it is a
    /// compatibility score rolled for the pair, because a rolled one would be indistinguishable
    /// from the disconnected flavour this whole model exists instead of.
    /// </remarks>
    private static double Warmth(WorldState world, Figure first, Figure second, int year)
    {
        double warmth = 0.10;

        FigureBond? bond = LifeStories.BondTo(first, second.Id);
        if (bond is not null)
        {
            BondKind warm = BondKind.Companion
                | BondKind.Patron
                | BondKind.Client
                | BondKind.Mentor
                | BondKind.Apprentice
                | BondKind.Guardian
                | BondKind.Ward;
            if ((bond.Kinds & warm) != BondKind.None) warmth += 0.22;

            warmth += Math.Max(0.0, bond.Affection) * 0.24;
            warmth += Math.Max(0.0, bond.Trust) * 0.18;
            warmth -= bond.Grievance * 0.40;
            warmth -= bond.Fear * 0.20;
        }

        if (first.Occupation == second.Occupation) warmth += 0.10;
        if (Shared(first, second)) warmth += 0.14;

        // Dispositions that are not opposed, on the axes a friendship would actually turn on. Not
        // similarity for its own sake: two people with the same appetite for war and the same
        // regard for custom have something to talk about, and the record says what both were.
        double apart =
            (Math.Abs(first.Disposition.Values.Aggression - second.Disposition.Values.Aggression)
                + Math.Abs(first.Disposition.Values.Piety - second.Disposition.Values.Piety)
                + Math.Abs(first.Disposition.Values.Tradition - second.Disposition.Values.Tradition))
            / 3.0;
        warmth += (1.0 - apart) * 0.16;

        int gap = Math.Abs(first.AgeIn(year) - second.AgeIn(year));
        if (gap > 20) warmth -= 0.10;
        if (gap > 40) warmth -= 0.10;

        // Rank. A crowned head and a resident of the same town are not two people who fall in
        // together, and letting them would make rulers everybody's friend — they are the figures
        // the chronicle records most and would therefore dominate every list.
        warmth -= Gulf(world, first, second) * 0.34;

        return DetMath.Clamp01(warmth);
    }

    /// <summary>Whether the two of them carry a memory of the same thing.</summary>
    /// <remarks>
    /// Cheap and exact: the memory lists are at most twelve long and every entry names the person,
    /// place, battle or route it is about, so two people who were in the same siege or lived
    /// through the same famine have an entry in common and the model can say so.
    /// </remarks>
    private static bool Shared(Figure first, Figure second)
    {
        foreach (SalientMemory mine in first.Memories)
        {
            if (mine.AboutId.IsNone) continue;
            if (mine.AboutId == second.Id) continue;

            foreach (SalientMemory theirs in second.Memories)
            {
                if (theirs.AboutId == mine.AboutId && theirs.Kind == mine.Kind) return true;
            }
        }

        return false;
    }

    /// <summary>How far apart in standing the two of them are, in [0, 1].</summary>
    private static double Gulf(WorldState world, Figure first, Figure second)
    {
        double gulf = Math.Abs(Standing(world, first) - Standing(world, second));
        return DetMath.Clamp01(gulf);
    }

    private static double Standing(WorldState world, Figure figure)
    {
        if (world.Civilizations.Contains(figure.CivilizationId)
            && world.Civilizations[figure.CivilizationId].CurrentRulerId == figure.Id)
        {
            return 1.00;
        }

        if (figure.Offices.Exists(office => office.ToYear is null)) return 0.55;
        if (!figure.DynastyId.IsNone) return 0.35;

        return 0.10;
    }

    /// <summary>What a person is in a position to do for somebody, which is who does the favour.</summary>
    private static double Means(Figure figure, int year)
    {
        double means = figure.Offices.Exists(office => office.ToYear is null) ? 0.50 : 0.0;
        if (!figure.DynastyId.IsNone) means += 0.25;
        if (figure.AgeIn(year) >= 40) means += 0.10;
        return means;
    }

    /// <summary>Which circumstance a beginning is credited to, from what the pair already share.</summary>
    private static AffinityOrigin Origin(FigureBond? bond)
    {
        if (bond is null) return AffinityOrigin.SharedResidence;
        if (bond.Kinds.HasFlag(BondKind.Companion)) return AffinityOrigin.SharedCampaign;

        BondKind service = BondKind.Patron
            | BondKind.Client
            | BondKind.Mentor
            | BondKind.Apprentice
            | BondKind.Guardian
            | BondKind.Ward;
        if ((bond.Kinds & service) != BondKind.None) return AffinityOrigin.SharedService;

        return AffinityOrigin.SharedResidence;
    }

    private static EventKind SourceOf(AffinityOrigin origin) => origin switch
    {
        AffinityOrigin.SharedCampaign => EventKind.BattleFought,
        AffinityOrigin.SharedService => EventKind.OfficeGranted,
        _ => EventKind.AcquaintanceFormed,
    };

    /// <summary>
    /// Whether the two are close enough for a friendship to survive at all.
    /// </summary>
    /// <remarks>
    /// A realm, for the reason a quarrel uses one: courts move, and requiring the same town to keep
    /// a friendship alive would end one every time a governor took up a posting. The same town is
    /// what is required to <em>advance</em> it, which is a different question and asked separately.
    /// </remarks>
    private static bool InReach(WorldState world, Figure first, Figure second) =>
        first.CivilizationId == second.CivilizationId
        && world.Civilizations.Contains(first.CivilizationId);

    /// <summary>Whether this person is somebody the model follows through an ordinary year.</summary>
    private static bool Eligible(WorldState world, Figure figure, int year)
    {
        if (!figure.IsAlive) return false;
        if (figure.AgeIn(year) < Succession.MajorityAge) return false;
        if (!world.Civilizations.Contains(figure.CivilizationId)) return false;

        return world.Civilizations[figure.CivilizationId].IsActive;
    }

    /// <summary>Whether there is room for another.</summary>
    private static bool Available(Figure figure)
    {
        int open = 0;
        foreach (FigureAffinity affinity in figure.Affinities)
        {
            if (affinity.IsOpen) open++;
        }

        return open < OpenCapacity;
    }

    /// <summary>
    /// Whether these two already have a record, of any age.
    /// </summary>
    /// <remarks>
    /// One record per pair, ever. A second one would make "how long these two have known each
    /// other" unanswerable from either page, which is most of what the record is for.
    /// </remarks>
    private static bool Known(Figure first, Figure second)
    {
        foreach (FigureAffinity affinity in first.Affinities)
        {
            if (affinity.Involves(second.Id)) return true;
        }

        return false;
    }

    private static int OpenedBy(Figure figure)
    {
        int opened = 0;
        foreach (FigureAffinity affinity in figure.Affinities)
        {
            if (affinity.OpenerId == figure.Id) opened++;
        }

        return opened;
    }

    /// <summary>
    /// One stream per pair, per year, per question.
    /// </summary>
    /// <remarks>
    /// Keyed on both people rather than on iteration order, so that what happens between two of
    /// them is the same whoever else shared their town that year.
    /// </remarks>
    private static IRng Fork(
        WorldState world, Figure first, Figure second, int year, string question)
    {
        // Ordered, because the pair is unordered for every question except who turned on whom, and
        // an unordered pair keyed by argument position would be two different streams.
        (EntityId low, EntityId high) = first.Id.CompareTo(second.Id) <= 0
            ? (first.Id, second.Id)
            : (second.Id, first.Id);

        return world.Root
            .Fork("affinity", low.ToDiscriminator())
            .Fork("with", high.ToDiscriminator())
            .Fork(question, year);
    }

    private static string OriginDetail(AffinityOrigin origin) => origin switch
    {
        AffinityOrigin.SharedCampaign => "having stood in the same line",
        AffinityOrigin.SharedService => "the service they shared",
        _ => "the town they shared",
    };

    private static string TurnDetail(bool quarrelling, bool plotting) =>
        plotting
            ? "a design on their life"
            : quarrelling
                ? "the quarrel between them"
                : "a wrong they held against them";

    /// <summary>How the rung reads on the friendship's own record, where both parties are named.</summary>
    private static string StageDetail(AffinityStage stage) => stage switch
    {
        AffinityStage.Kindness => "did them a good turn",
        AffinityStage.Confidence => "trusted them with something",
        AffinityStage.Friendship => "counted them a friend",
        _ => "came to know them",
    };

    /// <summary>
    /// The same act as a bare verb, for a chronicle line that supplies its own object.
    /// </summary>
    /// <remarks>
    /// Two spellings for the reason the quarrel ladder needs two: a template cannot capitalise, and
    /// the world line and the figure's own page need the verb with and without its pronoun.
    /// </remarks>
    private static string StageVerb(AffinityStage stage) => stage switch
    {
        AffinityStage.Kindness => "did a good turn for",
        AffinityStage.Confidence => "put their trust in",
        AffinityStage.Friendship => "came to count as a friend of",
        _ => "came to know",
    };

    private static string StageVerbCapitalised(AffinityStage stage) => stage switch
    {
        AffinityStage.Kindness => "Did a good turn for",
        AffinityStage.Confidence => "Put their trust in",
        AffinityStage.Friendship => "Came to count as a friend of",
        _ => "Came to know",
    };
}
