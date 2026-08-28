using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>Guardians, mentors, formative childhood events, and grounded raised-adult origins.</summary>
public sealed class UpbringingTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public UpbringingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void UpbringingsAreBoundedGroundedAndVisibleAcrossTheSeedPanel()
    {
        int guardians = 0;
        int activeGuardians = 0;
        int apprentices = 0;
        int divergentCareers = 0;
        int backgrounds = 0;
        int childhoodSieges = 0;
        bool mentorChangedAWeight = false;
        bool siegeChangedAWeight = false;
        var careerFamilies = new HashSet<CareerFamily>();
        var apprenticesByFamily = Enum.GetValues<CareerFamily>()
            .ToDictionary(family => family, _ => 0);

        foreach (ulong seed in Seeds)
        {
            HistoryRun run = HistoryRun.Execute(TestWorlds.Standard(seed));
            WorldState world = run.World;
            WorldExport export = run.ToExport();
            var exported = export.Figures.ToDictionary(figure => figure.Id);

            int seedGuardians = 0;
            int seedApprentices = 0;
            int seedBackgrounds = 0;
            int seedSieges = 0;

            foreach (Figure figure in world.Figures)
            {
                foreach (FigureGuardianship guardianship in figure.Guardianships)
                {
                    // Count each shared record from the ward's side only.
                    if (guardianship.WardId != figure.Id) continue;

                    guardians++;
                    seedGuardians++;
                    Assert.NotEqual(guardianship.GuardianId, guardianship.WardId);
                    Assert.True(world.Figures.Contains(guardianship.GuardianId));
                    Figure guardian = world.Figures[guardianship.GuardianId];
                    Assert.True(guardian.BirthYear + Succession.MajorityAge <= guardianship.StartYear);
                    Assert.True(guardian.DeathYear is null || guardian.DeathYear >= guardianship.StartYear);
                    Assert.True(figure.BirthYear + Succession.MajorityAge > guardianship.StartYear);
                    Assert.True(Exists(world, guardianship.LocationId));
                    Assert.Contains(guardianship, guardian.Guardianships);

                    FigureBond guardianBond = Assert.Single(
                        guardian.Bonds,
                        bond => bond.OtherId == figure.Id && bond.Kinds.HasFlag(BondKind.Guardian));
                    Assert.Contains(
                        figure.Bonds,
                        bond => bond.OtherId == guardian.Id && bond.Kinds.HasFlag(BondKind.Ward));
                    Assert.True(guardianBond.SinceYear <= guardianship.StartYear);

                    if (guardianship.IsActive)
                    {
                        activeGuardians++;
                        Assert.True(guardian.IsAlive && figure.IsAlive);
                        Assert.True(figure.AgeIn(world.EndYear) < Succession.MajorityAge);
                        Assert.Null(guardianship.EndYear);
                    }
                    else
                    {
                        Assert.NotNull(guardianship.EndYear);
                        Assert.True(guardianship.EndYear >= guardianship.StartYear);
                        if (guardianship.End == GuardianshipEnd.Majority)
                        {
                            Assert.Equal(
                                figure.BirthYear + Succession.MajorityAge,
                                guardianship.EndYear);
                        }
                    }

                    Assert.Contains(
                        exported[figure.Id].Guardianships,
                        item => item.GuardianId == guardian.Id
                            && item.StartYear == guardianship.StartYear);
                    Assert.Contains(
                        exported[guardian.Id].Guardianships,
                        item => item.WardId == figure.Id
                            && item.StartYear == guardianship.StartYear);
                }

                foreach (FigureMentorship mentorship in figure.Mentorships)
                {
                    if (mentorship.ApprenticeId != figure.Id) continue;

                    apprentices++;
                    seedApprentices++;
                    Assert.NotEqual(figure.Id, mentorship.MentorId);
                    Assert.True(world.Figures.Contains(mentorship.MentorId));
                    Assert.True(Exists(world, mentorship.LocationId));

                    Figure mentor = world.Figures[mentorship.MentorId];
                    Assert.Contains(mentorship, mentor.Mentorships);
                    Assert.True(mentor.BirthYear + Succession.MajorityAge <= mentorship.StartYear);
                    Assert.True(mentor.DeathYear is null || mentor.DeathYear >= mentorship.StartYear);
                    Assert.True(mentor.BirthYear <= figure.BirthYear - 8);
                    Assert.Contains(
                        figure.Bonds,
                        bond => bond.OtherId == mentor.Id
                            && bond.Kinds.HasFlag(BondKind.Apprentice));

                    CareerFamily family = mentorship.CareerFamily;
                    careerFamilies.Add(family);
                    apprenticesByFamily[family]++;
                    if (Upbringings.FamilyOf(figure.Occupation) != family) divergentCareers++;

                    double[] without = Occupations.Weights(world, figure, includeMentor: false);
                    double[] with = Occupations.Weights(world, figure, includeMentor: true);
                    if (!without.SequenceEqual(with)) mentorChangedAWeight = true;
                }

                if (figure.Background is { } background)
                {
                    backgrounds++;
                    seedBackgrounds++;
                    Assert.NotEqual(FigureOrigin.Unrecorded, figure.Origin);
                    Assert.True(Exists(world, background.OriginSettlementId));
                    Assert.True(Exists(world, background.InstitutionId));

                    Assert.DoesNotContain(
                        world.Chronicle.Events,
                        entry => entry.Year < background.IntroducedYear
                            && entry.References().Contains(figure.Id));

                    ExportBackground exportedBackground = Assert.IsType<ExportBackground>(
                        exported[figure.Id].Background);
                    Assert.Equal(background.IntroducedYear, exportedBackground.IntroducedYear);
                    Assert.Equal(background.InstitutionId, exportedBackground.InstitutionId);
                }

                int sieges = figure.Memories.Count(memory =>
                    memory.Kind == MemoryKind.Siege
                    && memory.Year < figure.BirthYear + Succession.MajorityAge);
                childhoodSieges += sieges;
                seedSieges += sieges;
                if (sieges > 0)
                {
                    double[] withoutSiege = Occupations.Weights(world, figure, includeSiege: false);
                    double[] withSiege = Occupations.Weights(world, figure, includeSiege: true);
                    if (!withoutSiege.SequenceEqual(withSiege)) siegeChangedAWeight = true;
                }
            }

            _output.WriteLine(
                $"seed {seed}: guardians={seedGuardians}, apprentices={seedApprentices}, "
                + $"backgrounds={seedBackgrounds}, childhood-sieges={seedSieges}");
        }

        _output.WriteLine(
            $"total guardians={guardians} (active={activeGuardians}), apprentices={apprentices}, "
            + $"backgrounds={backgrounds}, childhood-sieges={childhoodSieges}, "
            + $"families={string.Join(',', apprenticesByFamily.Select(pair => $"{pair.Key}={pair.Value}"))}, "
            + $"divergent-careers={divergentCareers}");

        Assert.True(guardians > 0, "No orphan ever received a guardian.");
        Assert.True(apprentices > 0, "No child ever received a mentor.");
        Assert.True(mentorChangedAWeight, "Mentorship never altered a downstream career weight.");
        Assert.True(divergentCareers > 0, "Every apprentice copied their mentor's career family.");
        Assert.Equal(Enum.GetValues<CareerFamily>().Length, careerFamilies.Count);
        Assert.True(backgrounds > 0, "No raised adult received a grounded background.");
        Assert.True(childhoodSieges > 0, "No child retained a formative siege memory.");
        Assert.True(siegeChangedAWeight, "A childhood siege never altered a downstream career weight.");
    }

    private static bool Exists(WorldState world, EntityId id) => id.Kind switch
    {
        EntityKind.Civilization => world.Civilizations.Contains(id),
        EntityKind.Settlement => world.Settlements.Contains(id),
        EntityKind.Figure => world.Figures.Contains(id),
        EntityKind.Religion => world.Religions.Contains(id),
        _ => false,
    };
}
