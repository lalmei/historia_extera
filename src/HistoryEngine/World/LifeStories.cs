using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>Builds the durable relationships and memories that events leave behind.</summary>
/// <remarks>
/// All bond mutation comes through this type. That makes reciprocity an invariant rather than a
/// convention each caller has to remember, and keeps the numerical bounds identical on both
/// sides. Memories are deliberately few: the chronicle is the complete record; this list is what
/// a person still acts on and what a reader should see before opening that record.
/// </remarks>
public static class LifeStories
{
    public const int MemoryCapacity = 12;

    public const double ActiveMemoryThreshold = 0.18;

    public const double FormativeMemoryThreshold = 0.72;

    private const double MemoryFadePerYear = 0.0125;

    public static void Marry(WorldState world, Figure first, Figure second, int year)
    {
        Relate(
            first, second,
            BondKind.Spouse, BondKind.Spouse, BondCause.Marriage, year,
            EventKind.FigureMarried, first.Id, world.ResidenceOf(first),
            affection: 0.42, trust: 0.28, obligation: 0.30);

        Remember(first, MemoryKind.Marriage, year, EventKind.FigureMarried, second.Id,
            world.ResidenceOf(first), 0.66);
        Remember(second, MemoryKind.Marriage, year, EventKind.FigureMarried, first.Id,
            world.ResidenceOf(second), 0.66);
    }

    public static void AddParent(WorldState world, Figure parent, Figure child, int year)
    {
        Relate(
            parent, child,
            BondKind.Kin | BondKind.Parent,
            BondKind.Kin | BondKind.Child,
            BondCause.Parenthood,
            year,
            EventKind.FigureBorn,
            child.Id,
            child.BirthSettlementId,
            affection: 0.36,
            trust: 0.18,
            obligation: 0.48,
            reciprocalAffection: 0.30,
            reciprocalObligation: 0.26);

        foreach (EntityId siblingId in parent.ChildIds)
        {
            if (siblingId == child.Id || !world.Figures.Contains(siblingId)) continue;

            Figure sibling = world.Figures[siblingId];
            if (!sibling.IsAlive) continue;
            FigureBond? existing = BondTo(child, sibling.Id);
            if (existing is not null && existing.Kinds.HasFlag(BondKind.Sibling)) continue;

            Relate(
                child, sibling,
                BondKind.Kin | BondKind.Sibling,
                BondKind.Kin | BondKind.Sibling,
                BondCause.Parenthood,
                year,
                EventKind.FigureBorn,
                child.Id,
                child.BirthSettlementId,
                affection: 0.24,
                trust: 0.15,
                obligation: 0.20);
        }

        Remember(parent, MemoryKind.Parenthood, year, EventKind.FigureBorn, child.Id,
            child.BirthSettlementId, 0.58);
    }

    public static void AddPatronage(
        Figure patron, Figure client, int year, EntityId location = default)
    {
        Relate(
            patron, client,
            BondKind.Patron,
            BondKind.Client,
            BondCause.Patronage,
            year,
            EventKind.OfficeGranted,
            client.Id,
            location,
            trust: 0.18,
            obligation: 0.10,
            reciprocalObligation: 0.35);

        Remember(client, MemoryKind.Gratitude, year, EventKind.OfficeGranted, patron.Id,
            location, 0.55);
    }

    public static void AddMentorship(
        Figure mentor,
        Figure apprentice,
        int year,
        CareerFamily careerFamily,
        EntityId location = default)
    {
        Relate(
            mentor, apprentice,
            BondKind.Mentor,
            BondKind.Apprentice,
            BondCause.Mentorship,
            year,
            EventKind.OccupationTaken,
            apprentice.Id,
            location,
            affection: 0.18,
            trust: 0.30,
            obligation: 0.10,
            reciprocalTrust: 0.35,
            reciprocalObligation: 0.28);

        Remember(apprentice, MemoryKind.Mentorship, year, EventKind.OccupationTaken, mentor.Id,
            location, 0.62);
        Remember(mentor, MemoryKind.Mentorship, year, EventKind.OccupationTaken, apprentice.Id,
            location, 0.42);

        var mentorship = new FigureMentorship(
            mentor.Id, apprentice.Id, year, careerFamily, location);
        mentor.Mentorships.Add(mentorship);
        apprentice.Mentorships.Add(mentorship);
    }

    public static void AddGuardianship(
        Figure guardian,
        Figure ward,
        int year,
        EventKind cause,
        EntityId causeEntity,
        EntityId location)
    {
        Relate(
            guardian, ward,
            BondKind.Guardian,
            BondKind.Ward,
            BondCause.Guardianship,
            year,
            cause,
            causeEntity,
            location,
            affection: 0.20,
            trust: 0.22,
            obligation: 0.46,
            reciprocalAffection: 0.18,
            reciprocalTrust: 0.32,
            reciprocalObligation: 0.20);
    }

    /// <summary>Marks the end of an active duty without erasing the historical bond.</summary>
    public static void EndGuardianship(
        Figure guardian, Figure ward, int year, EntityId location)
    {
        NoteBondChange(guardian, ward.Id, year, EventKind.GuardianshipEnded, ward.Id, location);
        NoteBondChange(ward, guardian.Id, year, EventKind.GuardianshipEnded, ward.Id, location);
    }

    public static void AddRivalry(
        Figure first,
        Figure second,
        int year,
        EventKind source,
        EntityId location = default,
        double grievance = 0.38,
        EntityId sourceEntity = default)
    {
        if (sourceEntity.IsNone) sourceEntity = first.Id;

        Relate(
            first, second,
            BondKind.Rival,
            BondKind.Rival,
            BondCause.Conflict,
            year,
            source,
            sourceEntity,
            location,
            affection: -0.24,
            trust: -0.30,
            grievance: grievance,
            reciprocalAffection: -0.18,
            reciprocalTrust: -0.24,
            reciprocalGrievance: grievance * 0.80);

        Remember(first, MemoryKind.Rivalry, year, source, second.Id, location, 0.58);
        Remember(second, MemoryKind.Rivalry, year, source, first.Id, location, 0.48);
    }

    /// <summary>
    /// Answers a grievance without erasing that there was one.
    /// </summary>
    /// <remarks>
    /// The <see cref="BondKind.Rival"/> role stays. Two people who quarrelled and made it up are
    /// not two people who never quarrelled, and the role is the record of what they have been to
    /// each other; what a settlement changes is how much of it they are still acting on.
    /// </remarks>
    public static void Reconcile(
        Figure first,
        Figure second,
        int year,
        EventKind source,
        EntityId location,
        double relief,
        bool warmly)
    {
        relief = DetMath.Clamp01(relief);

        Settle(BondTo(first, second.Id));
        Settle(BondTo(second, first.Id));

        if (!warmly) return;

        Remember(first, MemoryKind.Gratitude, year, source, second.Id, location, 0.44);
        Remember(second, MemoryKind.Gratitude, year, source, first.Id, location, 0.44);

        void Settle(FigureBond? bond)
        {
            if (bond is null) return;

            bond.Grievance = DetMath.Clamp01(bond.Grievance - relief);
            bond.Fear = DetMath.Clamp01(bond.Fear - (relief * 0.5));
            if (warmly)
            {
                bond.Affection = DetMath.Clamp(bond.Affection + (relief * 0.35), -1.0, 1.0);
                bond.Trust = DetMath.Clamp(bond.Trust + (relief * 0.30), -1.0, 1.0);
            }

            bond.LastCause = BondCause.Conflict;
            bond.LastChangedYear = year;
            bond.LastEventKind = source;
            bond.LastLocationId = location;
        }
    }

    /// <summary>Deepens an existing quarrel into declared enmity.</summary>
    public static void Embitter(
        Figure first,
        Figure second,
        int year,
        EventKind source,
        EntityId sourceEntity,
        EntityId location,
        double grievance,
        double fear = 0.0)
    {
        Relate(
            first, second,
            BondKind.Rival | BondKind.Enemy,
            BondKind.Rival | BondKind.Enemy,
            BondCause.Conflict,
            year,
            source,
            sourceEntity,
            location,
            affection: -0.18,
            trust: -0.22,
            fear: fear,
            grievance: grievance,
            reciprocalGrievance: grievance * 0.7,
            reciprocalFear: fear * 0.6);
    }

    // -----------------------------------------------------------------------
    // Friendship
    // -----------------------------------------------------------------------

    /// <summary>
    /// Records that two people have come to know each other, and nothing more than that.
    /// </summary>
    /// <remarks>
    /// Deliberately almost nothing. An acquaintance is not a friend, and writing one in at the
    /// warmth of a friendship would let the top of the ladder be reached by standing on the bottom
    /// rung of it. What it does buy is the provenance: from here the bond names the year and the
    /// town, so every rung above it is anchored to a circumstance the world actually recorded.
    /// </remarks>
    public static void AddAcquaintance(
        Figure first, Figure second, int year, EventKind source, EntityId location)
    {
        Relate(
            first, second,
            BondKind.None,
            BondKind.None,
            BondCause.Friendship,
            year,
            source,
            second.Id,
            location,
            affection: 0.06,
            trust: 0.05,
            reciprocalAffection: 0.06,
            reciprocalTrust: 0.05);
    }

    /// <summary>One of them did the other a good turn.</summary>
    /// <remarks>
    /// The obligation lands on the person who received it and the warmth on both, which is the
    /// asymmetry that makes a favour worth modelling separately from mutual regard. It is also the
    /// only rung that leaves a memory below the friendship itself, and it reuses
    /// <see cref="MemoryKind.Gratitude"/> rather than adding a kind, because a good turn done and
    /// an office granted are the same experience to the person who got them.
    /// </remarks>
    public static void AddFavour(
        Figure giver, Figure receiver, int year, EventKind source, EntityId location)
    {
        Relate(
            giver, receiver,
            BondKind.None,
            BondKind.None,
            BondCause.Friendship,
            year,
            source,
            receiver.Id,
            location,
            affection: 0.14,
            trust: 0.10,
            reciprocalAffection: 0.20,
            reciprocalTrust: 0.16,
            reciprocalObligation: 0.22);

        // Low on purpose. The memory list is twelve long and holds bereavements and wounds; a
        // favour between friends that evicted one of those would be a worse page, so this earns
        // its slot by being repeated rather than by arriving loud.
        Remember(receiver, MemoryKind.Gratitude, year, source, giver.Id, location, 0.34);
    }

    /// <summary>Something was entrusted that could have been kept back.</summary>
    public static void AddConfidence(
        Figure first, Figure second, int year, EventKind source, EntityId location)
    {
        Relate(
            first, second,
            BondKind.None,
            BondKind.None,
            BondCause.Friendship,
            year,
            source,
            second.Id,
            location,
            affection: 0.12,
            trust: 0.24,
            obligation: 0.08,
            reciprocalAffection: 0.12,
            reciprocalTrust: 0.24,
            reciprocalObligation: 0.08);
    }

    /// <summary>The rung at which the two of them would name the tie.</summary>
    public static void AddFriendship(
        Figure first, Figure second, int year, EventKind source, EntityId location)
    {
        Relate(
            first, second,
            BondKind.Friend,
            BondKind.Friend,
            BondCause.Friendship,
            year,
            source,
            second.Id,
            location,
            affection: 0.22,
            trust: 0.18,
            obligation: 0.10,
            reciprocalAffection: 0.22,
            reciprocalTrust: 0.18,
            reciprocalObligation: 0.10);

        Remember(first, MemoryKind.Friendship, year, source, second.Id, location, 0.52);
        Remember(second, MemoryKind.Friendship, year, source, first.Id, location, 0.52);
    }

    /// <summary>
    /// Keeps a standing friendship standing, without writing an event for a year in which the two
    /// of them merely went on being friends.
    /// </summary>
    /// <remarks>
    /// The alternative was to let the stale clock cool every friendship twelve years after it was
    /// made, which would mean the engine had no friendships that lasted. What keeps this honest is
    /// that the caller only reaches it while the two are still within reach of each other: friends
    /// who end up in different towns stop being reinforced and the clock runs again.
    /// </remarks>
    public static void Warm(
        Figure first, Figure second, int year, EventKind source, EntityId location)
    {
        Relate(
            first, second,
            BondKind.None,
            BondKind.None,
            BondCause.Friendship,
            year,
            source,
            second.Id,
            location,
            affection: 0.03,
            trust: 0.02,
            reciprocalAffection: 0.03,
            reciprocalTrust: 0.02);

        Reinforce(first, MemoryKind.Friendship, second.Id, year);
        Reinforce(second, MemoryKind.Friendship, first.Id, year);
    }

    /// <summary>
    /// One friend turned on the other.
    /// </summary>
    /// <remarks>
    /// <para><see cref="BondKind.Friend"/> stays, for the reason <see cref="BondKind.Rival"/> stays
    /// through a reconciliation: two people who were friends and are now enemies are not two people
    /// who were never friends, and that they were is the whole weight of the thing.</para>
    ///
    /// <para>The second way into <see cref="MemoryKind.Betrayal"/>, and the first that does not
    /// require having joined a plot. Until now the only person who could be betrayed was a
    /// conspirator informed on by their own accomplice, so the experience was reserved for people
    /// who had already put themselves outside the law — while the ordinary case, a trust freely
    /// given to somebody who then used it, had nothing to give it.</para>
    /// </remarks>
    public static void Betray(
        Figure betrayer,
        Figure betrayed,
        int year,
        EventKind source,
        EntityId location,
        double weight)
    {
        weight = DetMath.Clamp01(weight);

        Relate(
            betrayed, betrayer,
            BondKind.Rival | BondKind.Enemy | BondKind.Betrayer,
            BondKind.Rival,
            BondCause.Betrayal,
            year,
            source,
            betrayer.Id,
            location,
            affection: -0.55 * weight,
            trust: -0.80 * weight,
            grievance: 0.62 * weight,
            reciprocalAffection: -0.30 * weight,
            reciprocalTrust: -0.24 * weight,
            reciprocalGrievance: 0.10 * weight);

        FigureBond? wronged = BondTo(betrayed, betrayer.Id);
        if (wronged is not null) wronged.Obligation = 0.0;

        Remember(
            betrayed,
            MemoryKind.Betrayal,
            year,
            source,
            betrayer.Id,
            location,
            DetMath.Clamp01(0.62 + (0.30 * weight)));
    }

    /// <summary>Pushes an existing memory's clock forward without adding one that is not there.</summary>
    private static void Reinforce(Figure figure, MemoryKind kind, EntityId about, int year)
    {
        SalientMemory? existing = figure.Memories.Find(
            memory => memory.Kind == kind && memory.AboutId == about);
        if (existing is null) return;

        existing.LastReinforcedYear = year;
    }

    public static void AddConspirators(Figure leader, Figure recruit, int year)
    {
        Relate(
            leader, recruit,
            BondKind.CoConspirator,
            BondKind.CoConspirator,
            BondCause.Conspiracy,
            year,
            EventKind.UndertakingStarted,
            leader.Id,
            EntityId.None,
            trust: 0.24,
            obligation: 0.12,
            reciprocalObligation: 0.20);

        Remember(
            leader, MemoryKind.Conspiracy, year, EventKind.UndertakingStarted,
            recruit.Id, intensity: 0.58);
        Remember(
            recruit, MemoryKind.Conspiracy, year, EventKind.UndertakingStarted,
            leader.Id, intensity: 0.68);
    }

    /// <summary>Turns one resolved engagement into pride, fear, comradeship and lasting wounds.</summary>
    public static void ResolveBattle(WorldState world, Battle battle, int year)
    {
        var participants = new List<(Figure Figure, CampaignMemory Memory)>();
        EventKind source = battle.SiegeOutcome == SiegeOutcome.Lifted
            ? EventKind.SiegeLifted
            : EventKind.BattleFought;

        foreach (EntityId figureId in battle.WitnessIds)
        {
            if (!world.Figures.Contains(figureId)) continue;

            Figure figure = world.Figures[figureId];
            CampaignMemory? memory = figure.Campaigns.Find(item => item.BattleId == battle.Id);
            if (memory is null) continue;

            participants.Add((figure, memory));

            bool triumphant = memory.Triumphant == true;
            Remember(
                figure,
                triumphant ? MemoryKind.Triumph : MemoryKind.Defeat,
                year,
                source,
                battle.Id,
                BattlePlace(battle),
                triumphant ? 0.64 : 0.72);
            if (source == EventKind.BattleFought)
            {
                Undertakings.NoteBattle(world, figure, memory, battle, year);
            }
        }

        for (int i = 0; i < participants.Count; i++)
        {
            for (int j = i + 1; j < participants.Count; j++)
            {
                (Figure first, CampaignMemory firstMemory) = participants[i];
                (Figure second, CampaignMemory secondMemory) = participants[j];

                if (firstMemory.SideId == secondMemory.SideId)
                {
                    AddCompanionship(
                        first, second, year, source, battle.Id, BattlePlace(battle));
                }
            }
        }

        if (world.Figures.Contains(battle.AttackerCommanderId)
            && world.Figures.Contains(battle.DefenderCommanderId))
        {
            AddRivalry(
                world.Figures[battle.AttackerCommanderId],
                world.Figures[battle.DefenderCommanderId],
                year,
                source,
                BattlePlace(battle),
                0.24,
                battle.Id);
        }
    }

    /// <summary>Zero while bedridden; reduced after a permanent wound; otherwise one.</summary>
    public static double Fitness(Figure figure, int year)
    {
        bool permanent = false;
        foreach (FigureInjury injury in figure.Injuries)
        {
            if (injury.IsRecovering(year)) return 0.0;
            if (injury.Permanent) permanent = true;
        }

        return permanent ? 0.68 : 1.0;
    }

    /// <summary>Leaves a death with everyone in the household it touched.</summary>
    public static void Bereave(
        WorldState world,
        Figure deceased,
        IReadOnlyList<Figure> family,
        int year,
        DeathCause cause)
    {
        bool violent = cause is DeathCause.Battle
            or DeathCause.Assassination
            or DeathCause.Execution
            or DeathCause.Poisoning
            or DeathCause.Duel;

        foreach (Figure survivor in family)
        {
            Relate(
                survivor, deceased,
                BondKind.Kin, BondKind.Kin, BondCause.Bereavement, year,
                EventKind.FigureDied, deceased.Id, world.ResidenceOf(deceased));

            FigureBond bond = BondTo(survivor, deceased.Id)!;
            if (violent) bond.Fear = DetMath.Clamp01(bond.Fear + 0.18);

            Remember(
                survivor,
                MemoryKind.Bereavement,
                year,
                EventKind.FigureDied,
                deceased.Id,
                world.ResidenceOf(deceased),
                violent ? 0.94 : 0.78);

            Undertakings.ConsiderBereavementVow(world, survivor, deceased, year);
        }
    }

    /// <summary>Adds or reinforces one memory and evicts the least salient when the cap is crossed.</summary>
    public static void Remember(
        Figure figure,
        MemoryKind kind,
        int year,
        EventKind source,
        EntityId about = default,
        EntityId location = default,
        double intensity = 0.5)
    {
        intensity = DetMath.Clamp01(intensity);
        if (about.IsNone && location.IsNone)
        {
            throw new ArgumentException(
                "A salient memory must name the person, place, battle, war, or artifact that caused it.",
                nameof(about));
        }

        SalientMemory? existing = figure.Memories.Find(
            memory => memory.Kind == kind && memory.AboutId == about);

        if (existing is not null)
        {
            double faded = EffectiveIntensity(existing, year);
            existing.Intensity = DetMath.Clamp01(faded + (intensity * (1.0 - faded)));
            existing.LastReinforcedYear = year;
            existing.SourceKind = source;
            if (!location.IsNone) existing.LocationId = location;
            return;
        }

        figure.Memories.Add(new SalientMemory(kind, year, source, about, location, intensity));
        if (figure.Memories.Count <= MemoryCapacity) return;

        int weakest = 0;
        for (int i = 1; i < figure.Memories.Count; i++)
        {
            if (LessSalient(figure.Memories[i], figure.Memories[weakest], year)) weakest = i;
        }

        figure.Memories.RemoveAt(weakest);
    }

    /// <summary>Current strength after deterministic linear fading.</summary>
    public static double EffectiveIntensity(SalientMemory memory, int year) =>
        DetMath.Clamp01(
            memory.Intensity - (Math.Max(0, year - memory.LastReinforcedYear) * MemoryFadePerYear));

    public static bool IsActive(SalientMemory memory, int year) =>
        EffectiveIntensity(memory, year) >= ActiveMemoryThreshold;

    public static bool IsFormative(SalientMemory memory) =>
        memory.Intensity >= FormativeMemoryThreshold;

    /// <summary>Derives readable emotional state without introducing another mutable cache.</summary>
    public static FeelingState Feelings(Figure figure, int year)
    {
        double grief = 0.0;
        double fear = 0.0;
        double anger = 0.0;
        double pride = 0.0;
        double loyalty = 0.0;

        foreach (SalientMemory memory in figure.Memories)
        {
            double weight = EffectiveIntensity(memory, year);
            if (weight < ActiveMemoryThreshold) continue;

            switch (memory.Kind)
            {
                case MemoryKind.Bereavement:
                    grief += weight;
                    fear += weight * 0.20;
                    break;
                case MemoryKind.Injury:
                case MemoryKind.Defeat:
                case MemoryKind.Siege:
                    fear += weight * 0.72;
                    anger += weight * 0.28;
                    break;
                case MemoryKind.Humiliation:
                case MemoryKind.Betrayal:
                case MemoryKind.Rivalry:
                    anger += weight * 0.78;
                    break;
                case MemoryKind.Triumph:
                    pride += weight;
                    break;
                case MemoryKind.Gratitude:
                case MemoryKind.Mentorship:
                    loyalty += weight * 0.76;
                    break;
            }
        }

        Disposition disposition = figure.Disposition;
        double reflective = (disposition.Values.Tradition + disposition.Values.Piety) * 0.5;
        double dutiful =
            (disposition.Values.Tradition + (1.0 - disposition.Independence)) * 0.5;

        return new FeelingState(
            Temper(grief, DetMath.Lerp(0.82, 1.18, reflective)),
            Temper(fear, DetMath.Lerp(1.30, 0.70, disposition.Values.Aggression)),
            Temper(anger, DetMath.Lerp(0.70, 1.30, disposition.Values.Aggression)),
            Temper(pride, DetMath.Lerp(0.80, 1.20, disposition.Centralism)),
            Temper(loyalty, DetMath.Lerp(0.72, 1.28, dutiful)));
    }

    public static FigureBond? BondTo(Figure figure, EntityId other) =>
        figure.Bonds.Find(bond => bond.OtherId == other);

    private static void NoteBondChange(
        Figure figure,
        EntityId other,
        int year,
        EventKind source,
        EntityId entity,
        EntityId location)
    {
        FigureBond? bond = BondTo(figure, other);
        if (bond is null) return;

        bond.LastChangedYear = year;
        bond.LastCause = BondCause.Guardianship;
        bond.LastEventKind = source;
        bond.LastEntityId = entity;
        bond.LastLocationId = location;
    }

    private static void Relate(
        Figure first,
        Figure second,
        BondKind firstKinds,
        BondKind secondKinds,
        BondCause cause,
        int year,
        EventKind sourceKind,
        EntityId sourceEntity,
        EntityId sourceLocation,
        double affection = 0.0,
        double trust = 0.0,
        double obligation = 0.0,
        double fear = 0.0,
        double grievance = 0.0,
        double? reciprocalAffection = null,
        double? reciprocalTrust = null,
        double? reciprocalObligation = null,
        double? reciprocalFear = null,
        double? reciprocalGrievance = null)
    {
        if (first.Id == second.Id) return;

        Change(
            EnsureBond(first, second, year, sourceKind, sourceEntity, sourceLocation),
            firstKinds, cause, year, sourceKind, sourceEntity, sourceLocation,
            affection, trust, obligation, fear, grievance);
        Change(
            EnsureBond(second, first, year, sourceKind, sourceEntity, sourceLocation),
            secondKinds, cause, year, sourceKind, sourceEntity, sourceLocation,
            reciprocalAffection ?? affection,
            reciprocalTrust ?? trust,
            reciprocalObligation ?? obligation,
            reciprocalFear ?? fear,
            reciprocalGrievance ?? grievance);
    }

    private static void AddCompanionship(
        Figure first,
        Figure second,
        int year,
        EventKind source,
        EntityId battleId,
        EntityId location)
    {
        Relate(
            first, second,
            BondKind.Companion,
            BondKind.Companion,
            BondCause.SharedCampaign,
            year,
            source,
            battleId,
            location,
            affection: 0.06,
            trust: 0.09,
            obligation: 0.05);
    }

    /// <summary>Records one nonfatal consequence selected by the campaign resolver.</summary>
    internal static bool Wound(
        WorldState world,
        Figure figure,
        CampaignMemory memory,
        Battle battle,
        int year,
        IRng fate,
        double risk)
    {
        if (!fate.Chance(risk)) return false;

        Injure(
            world,
            figure,
            battle.Id,
            EventKind.BattleFought,
            BattlePlace(battle),
            year,
            fate,
            new[] { battle.WarId });
        return true;
    }

    /// <summary>
    /// Writes one wound, wherever it was got.
    /// </summary>
    /// <remarks>
    /// Shared by the battle resolver and the quarrel resolver so that severity, recovery and the
    /// permanent tail are one rule rather than two that drift apart. The caller decides whether a
    /// wound happens at all; this decides how bad it was and what it leaves behind.
    /// </remarks>
    internal static FigureInjury Injure(
        WorldState world,
        Figure figure,
        EntityId causeId,
        EventKind sourceKind,
        EntityId location,
        int year,
        IRng fate,
        IReadOnlyList<EntityId>? extra = null,
        bool record = true,
        InjuryCause cause = InjuryCause.Violence)
    {
        double gravity = fate.NextDouble();
        InjurySeverity severity = gravity < 0.62
            ? InjurySeverity.Minor
            : gravity < 0.90
                ? InjurySeverity.Serious
                : InjurySeverity.Grievous;
        bool permanent = severity == InjurySeverity.Grievous && fate.Chance(0.24);
        int recovery = year + (severity switch
        {
            InjurySeverity.Minor => 1,
            InjurySeverity.Serious => 2,
            _ => 3,
        });
        string detail = InjuryDetail(severity, permanent, fate, cause);

        var injury = new FigureInjury(
            causeId, sourceKind, year, severity, recovery, permanent, detail);
        figure.Injuries.Add(injury);
        Remember(
            figure,
            MemoryKind.Injury,
            year,
            EventKind.FigureWounded,
            causeId,
            location,
            severity switch
            {
                InjurySeverity.Minor => 0.55,
                InjurySeverity.Serious => 0.76,
                _ => 0.94,
            });

        // A quarrel writes its own event naming both parties, and a second line saying one of
        // them was hurt at the other would read as a separate incident on the same day.
        if (record)
        {
            world.Chronicle.Record(
                year,
                EventKind.FigureWounded,
                figure.Id,
                obj: causeId,
                location: location,
                extra: extra is null || extra.Count == 0 ? null : extra,
                data: Chronicle.Data(
                    ("severity", SeverityAdverb(severity)),
                    ("injury", detail),
                    ("permanent", permanent ? "true" : "false")));
        }

        return injury;
    }

    private static EntityId BattlePlace(Battle battle) =>
        battle.SettlementId.IsNone ? battle.RegionId : battle.SettlementId;

    /// <summary>
    /// What the wound was, in words that fit how it was got.
    /// </summary>
    /// <remarks>
    /// One wound model, two vocabularies. Severity, recovery and the permanent tail are shared by
    /// every way of being hurt and stay that way; only the prose forks, because it has to. A spear
    /// wound is the right description of a storming party and the wrong description of an
    /// earthquake, and the first thing this system did once it could hurt a survivor of a
    /// collapsing building was give one of them a deep spear wound.
    ///
    /// Both lists are the same length at every severity, so which vocabulary is used draws the same
    /// number of dice and cannot shift the stream.
    /// </remarks>
    private static string InjuryDetail(
        InjurySeverity severity, bool permanent, IRng rng, InjuryCause cause)
    {
        string[] minor = cause == InjuryCause.Calamity
            ? new[] { "a gash from falling stone", "a scalded arm", "a badly turned ankle" }
            : new[] { "a cut to the arm", "a bruised shoulder", "a glancing wound" };
        string[] serious = cause == InjuryCause.Calamity
            ? new[] { "a crushed foot", "deep burns to the arm", "a broken collarbone" }
            : new[] { "a broken leg", "a deep spear wound", "a crushed hand" };
        string[] grievous = cause == InjuryCause.Calamity
            ? new[]
            {
                "a shattered hip",
                "burns across the back",
                "a leg crushed under the fall",
            }
            : new[]
            {
                "a shattered knee",
                "the loss of an eye",
                "a wound through the chest",
            };

        string detail = severity switch
        {
            InjurySeverity.Minor => rng.Pick(minor),
            InjurySeverity.Serious => rng.Pick(serious),
            _ => rng.Pick(grievous),
        };

        return permanent ? detail + " that never fully healed" : detail;
    }

    private static string SeverityAdverb(InjurySeverity severity) => severity switch
    {
        InjurySeverity.Minor => "lightly",
        InjurySeverity.Serious => "seriously",
        _ => "grievously",
    };

    private static FigureBond EnsureBond(
        Figure owner,
        Figure other,
        int year,
        EventKind sourceKind,
        EntityId sourceEntity,
        EntityId sourceLocation)
    {
        FigureBond? found = BondTo(owner, other.Id);
        if (found is not null) return found;

        var bond = new FigureBond(
            other.Id, year, sourceKind, sourceEntity, sourceLocation);
        int before = owner.Bonds.FindIndex(existing => existing.OtherId.CompareTo(other.Id) > 0);
        if (before < 0) owner.Bonds.Add(bond);
        else owner.Bonds.Insert(before, bond);
        return bond;
    }

    private static void Change(
        FigureBond bond,
        BondKind kinds,
        BondCause cause,
        int year,
        EventKind sourceKind,
        EntityId sourceEntity,
        EntityId sourceLocation,
        double affection,
        double trust,
        double obligation,
        double fear,
        double grievance)
    {
        bond.Kinds |= kinds;
        bond.LastCause = cause;
        bond.LastChangedYear = year;
        bond.LastEventKind = sourceKind;
        bond.LastEntityId = sourceEntity;
        bond.LastLocationId = sourceLocation;
        bond.Affection = DetMath.Clamp(bond.Affection + affection, -1.0, 1.0);
        bond.Trust = DetMath.Clamp(bond.Trust + trust, -1.0, 1.0);
        bond.Obligation = DetMath.Clamp01(bond.Obligation + obligation);
        bond.Fear = DetMath.Clamp01(bond.Fear + fear);
        bond.Grievance = DetMath.Clamp01(bond.Grievance + grievance);
    }

    private static bool LessSalient(SalientMemory candidate, SalientMemory incumbent, int year)
    {
        int byStrength = EffectiveIntensity(candidate, year).CompareTo(
            EffectiveIntensity(incumbent, year));
        if (byStrength != 0) return byStrength < 0;

        int byReinforcement = candidate.LastReinforcedYear.CompareTo(incumbent.LastReinforcedYear);
        if (byReinforcement != 0) return byReinforcement < 0;

        int byKind = candidate.Kind.CompareTo(incumbent.Kind);
        if (byKind != 0) return byKind < 0;

        return candidate.AboutId.CompareTo(incumbent.AboutId) < 0;
    }

    private static double Temper(double feeling, double multiplier) =>
        DetMath.Clamp01(feeling * multiplier);
}
