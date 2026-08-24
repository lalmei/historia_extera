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

    private const double MemoryFadePerYear = 0.0125;

    public static void Marry(WorldState world, Figure first, Figure second, int year)
    {
        Relate(
            first, second,
            BondKind.Spouse, BondKind.Spouse, BondCause.Marriage, year,
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
            affection: 0.36,
            trust: 0.18,
            obligation: 0.48);

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
            trust: 0.18,
            obligation: 0.35);

        Remember(client, MemoryKind.Gratitude, year, EventKind.OfficeGranted, patron.Id,
            location, 0.55);
    }

    public static void AddMentorship(
        Figure mentor, Figure apprentice, int year, EntityId location = default)
    {
        Relate(
            mentor, apprentice,
            BondKind.Mentor,
            BondKind.Apprentice,
            BondCause.Mentorship,
            year,
            affection: 0.18,
            trust: 0.35,
            obligation: 0.28);

        Remember(apprentice, MemoryKind.Mentorship, year, EventKind.OccupationTaken, mentor.Id,
            location, 0.62);
        Remember(mentor, MemoryKind.Mentorship, year, EventKind.OccupationTaken, apprentice.Id,
            location, 0.42);
    }

    public static void AddRivalry(
        Figure first,
        Figure second,
        int year,
        EventKind source,
        EntityId location = default,
        double grievance = 0.38)
    {
        Relate(
            first, second,
            BondKind.Rival,
            BondKind.Rival,
            BondCause.Conflict,
            year,
            affection: -0.24,
            trust: -0.30,
            grievance: grievance);

        Remember(first, MemoryKind.Rivalry, year, source, second.Id, location, 0.58);
        Remember(second, MemoryKind.Rivalry, year, source, first.Id, location, 0.48);
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
            or DeathCause.Poisoning;

        foreach (Figure survivor in family)
        {
            Relate(
                survivor, deceased,
                BondKind.Kin, BondKind.Kin, BondCause.Bereavement, year);

            FigureBond bond = EnsureBond(survivor, deceased, year);
            if (violent) bond.Fear = DetMath.Clamp01(bond.Fear + 0.18);

            Remember(
                survivor,
                MemoryKind.Bereavement,
                year,
                EventKind.FigureDied,
                deceased.Id,
                world.ResidenceOf(deceased),
                violent ? 0.94 : 0.78);
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
            switch (memory.Kind)
            {
                case MemoryKind.Bereavement:
                    grief += weight;
                    fear += weight * 0.20;
                    break;
                case MemoryKind.Injury:
                case MemoryKind.Defeat:
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

        return new FeelingState(
            DetMath.Clamp01(grief),
            DetMath.Clamp01(fear),
            DetMath.Clamp01(anger),
            DetMath.Clamp01(pride),
            DetMath.Clamp01(loyalty));
    }

    public static FigureBond? BondTo(Figure figure, EntityId other) =>
        figure.Bonds.Find(bond => bond.OtherId == other);

    private static void Relate(
        Figure first,
        Figure second,
        BondKind firstKinds,
        BondKind secondKinds,
        BondCause cause,
        int year,
        double affection = 0.0,
        double trust = 0.0,
        double obligation = 0.0,
        double fear = 0.0,
        double grievance = 0.0)
    {
        if (first.Id == second.Id) return;

        Change(EnsureBond(first, second, year), firstKinds, cause, year,
            affection, trust, obligation, fear, grievance);
        Change(EnsureBond(second, first, year), secondKinds, cause, year,
            affection, trust, obligation, fear, grievance);
    }

    private static FigureBond EnsureBond(Figure owner, Figure other, int year)
    {
        FigureBond? found = BondTo(owner, other.Id);
        if (found is not null) return found;

        var bond = new FigureBond(other.Id, year);
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
        double affection,
        double trust,
        double obligation,
        double fear,
        double grievance)
    {
        bond.Kinds |= kinds;
        bond.LastCause = cause;
        bond.LastChangedYear = year;
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
}
