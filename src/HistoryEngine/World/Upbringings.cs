using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>Guardians, mentors, and the few formative facts that precede a public career.</summary>
/// <remarks>
/// Childhood is not another annual simulation. This type acts only at a parent's death, at the end
/// of a guardianship, and at majority. The resulting bonds and memories are durable; the chronicle
/// receives only the relationship boundaries and the genuinely formative event itself.
/// </remarks>
public static class Upbringings
{
    private const double MentorshipChance = 0.32;

    /// <summary>Closes duties affected by a death and gives an orphan a replacement where possible.</summary>
    public static void OnDeath(WorldState world, Figure deceased, int year)
    {
        var affected = new List<FigureGuardianship>();
        foreach (FigureGuardianship guardianship in deceased.Guardianships)
        {
            if (guardianship.IsActive) affected.Add(guardianship);
        }

        foreach (FigureGuardianship guardianship in affected)
        {
            bool guardianDied = guardianship.GuardianId == deceased.Id;
            End(
                world,
                guardianship,
                year,
                guardianDied ? GuardianshipEnd.GuardianDied : GuardianshipEnd.WardDied);

            if (!guardianDied || !world.Figures.Contains(guardianship.WardId)) continue;

            Figure ward = world.Figures[guardianship.WardId];
            if (ward.IsAlive && ward.AgeIn(year) < Succession.MajorityAge)
            {
                EnsureGuardian(world, ward, year, EventKind.FigureDied, deceased.Id);
            }
        }

        foreach (EntityId childId in deceased.ChildIds)
        {
            if (!world.Figures.Contains(childId)) continue;

            Figure child = world.Figures[childId];
            if (!child.IsAlive || child.AgeIn(year) >= Succession.MajorityAge) continue;

            EnsureGuardian(world, child, year, EventKind.FigureDied, deceased.Id);
        }
    }

    /// <summary>Ends an active guardianship once its ward can act in their own right.</summary>
    public static void EndAtMajority(WorldState world, Figure ward, int year)
    {
        if (ward.AgeIn(year) < Succession.MajorityAge) return;

        var active = new List<FigureGuardianship>();
        foreach (FigureGuardianship guardianship in ward.Guardianships)
        {
            if (guardianship.IsActive && guardianship.WardId == ward.Id) active.Add(guardianship);
        }

        foreach (FigureGuardianship guardianship in active)
        {
            End(world, guardianship, year, GuardianshipEnd.Majority);
        }
    }

    /// <summary>Assigns one best-grounded adult if this child has neither parent nor guardian present.</summary>
    internal static FigureGuardianship? EnsureGuardian(
        WorldState world,
        Figure ward,
        int year,
        EventKind causeKind,
        EntityId causeEntity)
    {
        if (!ward.IsAlive || ward.AgeIn(year) >= Succession.MajorityAge) return null;
        if (HasAvailableParent(world, ward) || ActiveGuardianship(ward) is not null) return null;

        Figure? guardian = FindGuardian(world, ward, year);
        if (guardian is null) return null;

        EntityId location = GroundedLocation(world, ward, guardian);
        var guardianship = new FigureGuardianship(
            guardian.Id, ward.Id, year, causeKind, causeEntity, location);

        guardian.Guardianships.Add(guardianship);
        ward.Guardianships.Add(guardianship);
        LifeStories.AddGuardianship(
            guardian, ward, year, causeKind, causeEntity.IsNone ? ward.Id : causeEntity, location);

        world.Chronicle.Record(
            year,
            EventKind.GuardianAssigned,
            guardian.Id,
            obj: ward.Id,
            location: location,
            extra: causeEntity.IsNone ? null : new[] { causeEntity },
            data: Chronicle.Data(("cause", "after their parents were no longer able to care for them")),
            significance: Significance.Routine);

        return guardianship;
    }

    /// <summary>Offers a child one reachable older practitioner in the year they come of age.</summary>
    public static Figure? FindMentor(WorldState world, Figure apprentice, int year)
    {
        if (!apprentice.IsAlive || apprentice.AgeIn(year) != Succession.MajorityAge) return null;

        foreach (FigureBond bond in apprentice.Bonds)
        {
            if (bond.Kinds.HasFlag(BondKind.Apprentice)) return null;
        }

        if (!world.Root
            .Fork("mentorship", apprentice.Id.ToDiscriminator())
            .Chance(MentorshipChance))
        {
            return null;
        }

        Figure? best = null;
        double bestScore = double.MinValue;

        foreach (Figure candidate in world.Figures)
        {
            if (!candidate.IsAlive || candidate.Id == apprentice.Id) continue;
            if (candidate.AgeIn(year) < apprentice.AgeIn(year) + 8) continue;
            if (candidate.Occupation == Occupation.None) continue;

            CareerFamily family = FamilyOf(candidate.Occupation);
            EntityId apprenticeHome = world.ResidenceOf(apprentice);
            bool samePlace = !apprenticeHome.IsNone
                && world.ResidenceOf(candidate) == apprenticeHome;
            if (!samePlace && !SharesInstitution(apprentice, candidate, family)) continue;

            double score = CareerFit(apprentice, family) * 10.0;
            if (samePlace) score += 4.0;
            if (candidate.CurrentOffice is not null) score += 1.5;
            if (candidate.DynastyId == apprentice.DynastyId && !candidate.DynastyId.IsNone) score += 1.0;

            if (best is null
                || score > bestScore
                || (score == bestScore && candidate.Id.CompareTo(best.Id) < 0))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    public static CareerFamily FamilyOf(Occupation occupation) => occupation switch
    {
        Occupation.Soldiery => CareerFamily.Arms,
        Occupation.Clergy => CareerFamily.Faith,
        Occupation.Townsfolk or Occupation.Guild or Occupation.Merchant => CareerFamily.TradeCraft,
        _ => CareerFamily.LettersOffice,
    };

    /// <summary>The concrete place or institution that makes a mentorship reachable.</summary>
    public static EntityId MentorshipLocation(WorldState world, Figure apprentice, Figure mentor)
    {
        EntityId home = world.ResidenceOf(apprentice);
        if (!home.IsNone && home == world.ResidenceOf(mentor)) return home;

        CareerFamily family = FamilyOf(mentor.Occupation);
        if (family == CareerFamily.Faith && !apprentice.ReligionId.IsNone)
        {
            return apprentice.ReligionId;
        }

        return apprentice.CivilizationId;
    }

    private static Figure? FindGuardian(WorldState world, Figure ward, int year)
    {
        Figure? best = null;
        int bestScore = int.MinValue;

        foreach (Figure candidate in world.Figures)
        {
            if (!candidate.IsAlive || candidate.Id == ward.Id) continue;
            if (candidate.AgeIn(year) < Succession.MajorityAge) continue;
            if (candidate.CivilizationId != ward.CivilizationId) continue;

            FigureBond? bond = LifeStories.BondTo(ward, candidate.Id);
            bool kin = bond is not null && bond.Kinds.HasFlag(BondKind.Kin);
            bool linkedToParent = LinkedToParent(ward, candidate);
            bool officeOrFaith = GroundsInstitution(world, ward, candidate);
            if (!kin && !linkedToParent && !officeOrFaith) continue;

            int score = kin ? 100 : linkedToParent ? 60 : 30;
            if (world.ResidenceOf(candidate) == world.ResidenceOf(ward)) score += 20;
            if (candidate.CurrentOffice is not null) score += 6;
            score += Math.Min(10, candidate.AgeIn(year) / 8);

            if (best is null
                || score > bestScore
                || (score == bestScore && candidate.Id.CompareTo(best.Id) < 0))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool HasAvailableParent(WorldState world, Figure ward)
    {
        foreach (EntityId parentId in ward.Parents())
        {
            if (!world.Figures.Contains(parentId)) continue;

            Figure parent = world.Figures[parentId];
            if (parent.IsAlive && parent.CivilizationId == ward.CivilizationId) return true;
        }

        return false;
    }

    private static FigureGuardianship? ActiveGuardianship(Figure ward) =>
        ward.Guardianships.Find(item => item.WardId == ward.Id && item.IsActive);

    private static bool LinkedToParent(Figure ward, Figure candidate)
    {
        foreach (EntityId parentId in ward.Parents())
        {
            FigureBond? bond = LifeStories.BondTo(candidate, parentId);
            if (bond is null) continue;

            BondKind grounded = BondKind.Spouse | BondKind.Patron | BondKind.Client | BondKind.Mentor;
            if ((bond.Kinds & grounded) != BondKind.None) return true;
        }

        return false;
    }

    private static bool GroundsInstitution(WorldState world, Figure ward, Figure candidate)
    {
        EntityId home = world.ResidenceOf(ward);
        OfficeHolding? office = candidate.CurrentOffice;
        if (office is not null)
        {
            if (office.ScopeId == home) return true;
            if (office.CivilizationId == ward.CivilizationId
                && office.Kind is OfficeKind.Ruler or OfficeKind.Regent or OfficeKind.Marshal)
            {
                return true;
            }
        }

        return !ward.ReligionId.IsNone
            && candidate.ReligionId == ward.ReligionId
            && candidate.Occupation == Occupation.Clergy;
    }

    private static EntityId GroundedLocation(WorldState world, Figure ward, Figure guardian)
    {
        EntityId wardHome = world.ResidenceOf(ward);
        if (!wardHome.IsNone) return wardHome;

        EntityId guardianHome = world.ResidenceOf(guardian);
        if (!guardianHome.IsNone) return guardianHome;

        if (guardian.CurrentOffice is { } office && !office.ScopeId.IsNone) return office.ScopeId;
        if (!ward.ReligionId.IsNone && ward.ReligionId == guardian.ReligionId)
        {
            return ward.ReligionId;
        }

        return ward.CivilizationId;
    }

    private static bool SharesInstitution(
        Figure apprentice, Figure candidate, CareerFamily family)
    {
        if (candidate.CivilizationId != apprentice.CivilizationId) return false;

        return family switch
        {
            CareerFamily.Arms => true,
            CareerFamily.Faith => !apprentice.ReligionId.IsNone
                && apprentice.ReligionId == candidate.ReligionId,
            CareerFamily.LettersOffice => true,
            _ => false,
        };
    }

    private static double CareerFit(Figure apprentice, CareerFamily family) => family switch
    {
        CareerFamily.Arms =>
            apprentice.Disposition.Values.Aggression + apprentice.Disposition.Values.Expansionism,
        CareerFamily.Faith =>
            apprentice.Disposition.Values.Piety + (apprentice.Disposition.Values.Tradition * 0.30),
        CareerFamily.TradeCraft =>
            apprentice.Disposition.Values.Mercantile + (apprentice.Disposition.Values.Tradition * 0.25),
        _ => apprentice.Disposition.Values.Learning + (apprentice.Disposition.Centralism * 0.25),
    };

    private static void End(
        WorldState world, FigureGuardianship guardianship, int year, GuardianshipEnd ending)
    {
        if (!guardianship.IsActive) return;

        guardianship.End = ending;
        guardianship.EndYear = year;

        if (!world.Figures.Contains(guardianship.GuardianId)
            || !world.Figures.Contains(guardianship.WardId))
        {
            return;
        }

        Figure guardian = world.Figures[guardianship.GuardianId];
        Figure ward = world.Figures[guardianship.WardId];
        LifeStories.EndGuardianship(guardian, ward, year, guardianship.LocationId);

        string cause = ending switch
        {
            GuardianshipEnd.Majority => "when the ward came of age",
            GuardianshipEnd.GuardianDied => "with the guardian's death",
            _ => "with the ward's death",
        };

        world.Chronicle.Record(
            year,
            EventKind.GuardianshipEnded,
            guardian.Id,
            obj: ward.Id,
            location: guardianship.LocationId,
            data: Chronicle.Data(("cause", cause)),
            significance: Significance.Routine);
    }
}
