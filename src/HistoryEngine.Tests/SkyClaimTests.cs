using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// What people said the lights were, and what the sky made of the saying.
/// </summary>
public sealed class SkyClaimTests
{
    /// <summary>
    /// Resampled when persistent conspiracies landed and moved every history. Nothing here reads
    /// the political model; these are simply seeds whose skies still carry both registers and
    /// every verdict, including the early refutation the panel exists to reach.
    /// </summary>
    private static readonly ulong[] Seeds = { 6, 17, 29, 46, 47 };

    private readonly ITestOutputHelper _output;

    public SkyClaimTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Nobody claims anything they had no reason to claim.
    /// </summary>
    /// <remarks>
    /// Every claim rests on sightings its claimant made, in a year they were alive, about a comet
    /// the sky actually returns. A measured one additionally states the interval it derived and
    /// names the year that interval implies — so the arithmetic on the page is the arithmetic the
    /// verdict is passed on.
    /// </remarks>
    [Fact]
    public void EveryClaimRestsOnSightingsItsClaimantActuallyMade()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (SkyClaim claim in figure.Claims)
                {
                    Assert.Equal(figure.Id, claim.ClaimantId);
                    Assert.NotEmpty(claim.RestsOnYears);
                    Assert.All(claim.RestsOnYears, seen => Assert.True(seen <= claim.Year));
                    Assert.Contains(
                        figure.Observations,
                        seen => seen.CometIndex == claim.CometIndex && seen.Year == claim.Year);
                    Assert.False(string.IsNullOrWhiteSpace(claim.Reading));

                    if (claim.Register == ClaimRegister.Measured)
                    {
                        Assert.True(claim.IntervalYears > 0);
                        Assert.Equal(claim.Year + claim.IntervalYears, claim.PredictedYear);
                    }
                    else
                    {
                        Assert.Null(claim.PredictedYear);
                        Assert.Equal(ClaimVerdict.NotTestable, claim.Verdict);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A measured claim is settled by the orbit and by nothing about its claimant.
    /// </summary>
    /// <remarks>
    /// The assertion this whole milestone exists for. Recompute every verdict straight from the
    /// rolled sky and require the engine to have reached the same answer. If it ever diverges, then
    /// something other than the sky is deciding who was right, and a prediction in this world means
    /// no more than an assertion.
    /// </remarks>
    [Fact]
    public void TheSkySettlesItAndNothingElseDoes()
    {
        int checkedVerdicts = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            WorldCosmology sky = world.Flavour.Cosmology;

            foreach (Figure figure in world.Figures)
            {
                foreach (SkyClaim claim in figure.Claims)
                {
                    if (claim.Verdict is ClaimVerdict.NotTestable or ClaimVerdict.Untested) continue;

                    int predicted = Assert.IsType<int>(claim.PredictedYear);
                    int settled = Assert.IsType<int>(claim.SettledYear);
                    SystemComet comet = sky.Comets.Single(item => item.Index == claim.CometIndex);

                    if (claim.Verdict == ClaimVerdict.Confirmed)
                    {
                        Assert.Equal(predicted, settled);
                        Assert.True(
                            Skywatch.ReturnsIn(sky, comet, settled, world.StartYear, 1),
                            $"Seed {seed}: a claim was confirmed in {settled}, and the comet was "
                            + "not there.");
                    }
                    else
                    {
                        // Either the year it named came and went, or the comet was back before its
                        // period allowed — the return that shows the period is wrong.
                        bool early = settled < predicted;
                        Assert.True(
                            early
                                ? Skywatch.ReturnsIn(sky, comet, settled, world.StartYear, 0)
                                : !Skywatch.ReturnsIn(sky, comet, settled, world.StartYear, 1),
                            $"Seed {seed}: a claim was refuted in {settled} for no reason the sky "
                            + "gives.");
                        if (early) Assert.True(settled > claim.Year);
                    }

                    checkedVerdicts++;
                }
            }
        }

        _output.WriteLine($"verdicts recomputed from the orbit: {checkedVerdicts}");
        Assert.True(checkedVerdicts > 0, "No claim across the panel was ever settled.");
    }

    /// <summary>
    /// A period that is too long is refuted by the return it says cannot happen.
    /// </summary>
    /// <remarks>
    /// <para>The mechanism that makes adjudication worth having, and it took a wrong turn first.
    /// Checking only whether the comet arrived in the year named makes refutation unreachable:
    /// every interval anybody derives is a whole multiple of the true period, so a doubled period
    /// still names a year the comet is genuinely there. What falsifies it is the return in
    /// between.</para>
    ///
    /// <para>Seed 11 carries the case: someone derives fifty-six years for a comet on twenty-eight,
    /// because their realm missed one, and is refuted twenty-eight years later when it comes back
    /// early.</para>
    /// </remarks>
    [Fact]
    public void AnIntervalTooLongIsRefutedByTheReturnItDeniedWasComing()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(17)).World;
        WorldCosmology sky = world.Flavour.Cosmology;

        var early = new List<SkyClaim>();
        foreach (Figure figure in world.Figures)
        {
            foreach (SkyClaim claim in figure.Claims)
            {
                if (claim.Verdict != ClaimVerdict.Refuted) continue;
                if (claim.SettledYear >= claim.PredictedYear) continue;

                early.Add(claim);
            }
        }

        Assert.NotEmpty(early);

        foreach (SkyClaim claim in early)
        {
            SystemComet comet = sky.Comets.Single(item => item.Index == claim.CometIndex);
            double truth = Skywatch.PeriodYears(sky, comet);

            // Their period was a whole multiple of the truth — honest arithmetic on a register with
            // a gap in it — and that is exactly why the sky caught them out.
            double turns = claim.IntervalYears / truth;
            Assert.True(
                Math.Abs(turns - Math.Round(turns)) < 0.05 && Math.Round(turns) >= 2.0,
                $"A claim of {claim.IntervalYears} years was refuted early for a comet on "
                + $"{truth:F1}, which is {turns:F2} returns and not a missed one.");

            _output.WriteLine(
                $"refuted early: claimed {claim.IntervalYears}y in {claim.Year} for a comet on "
                + $"{truth:F1}y, caught out in {claim.SettledYear}");
        }
    }

    /// <summary>
    /// Both registers occur, both verdicts are reachable, and claims stay rare.
    /// </summary>
    [Fact]
    public void BothRegistersAndBothVerdictsOccurAcrossThePanel()
    {
        var registers = new Dictionary<ClaimRegister, int>();
        var verdicts = new Dictionary<ClaimVerdict, int>();
        int posthumous = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            var claims = world.Figures.SelectMany(figure => figure.Claims).ToList();

            foreach (SkyClaim claim in claims)
            {
                registers[claim.Register] = registers.GetValueOrDefault(claim.Register) + 1;
                verdicts[claim.Verdict] = verdicts.GetValueOrDefault(claim.Verdict) + 1;
                if (claim.SettledYear is not null && !claim.ClaimantSawTheAnswer) posthumous++;
            }

            int lines = world.Chronicle.Events.Count(entry => entry.Kind is EventKind.SkyClaimMade
                or EventKind.SkyClaimConfirmed
                or EventKind.SkyClaimRefuted);

            _output.WriteLine($"seed {seed}: claims={claims.Count} lines={lines}");
            Assert.True(
                lines < world.Chronicle.Events.Count / 100,
                $"Seed {seed}: the sky is crowding the timeline with {lines} lines.");
        }

        _output.WriteLine("registers " + string.Join(", ", registers.Select(p => $"{p.Key}={p.Value}")));
        _output.WriteLine("verdicts  " + string.Join(", ", verdicts.Select(p => $"{p.Key}={p.Value}")));
        _output.WriteLine($"settled after the claimant's death: {posthumous}");

        Assert.True(registers.GetValueOrDefault(ClaimRegister.Mythic) > 0, "Nobody ever explained.");
        Assert.True(registers.GetValueOrDefault(ClaimRegister.Measured) > 0, "Nobody ever counted.");
        Assert.True(verdicts.GetValueOrDefault(ClaimVerdict.Confirmed) > 0, "Nobody was ever right.");
        Assert.True(verdicts.GetValueOrDefault(ClaimVerdict.Refuted) > 0, "Nobody was ever wrong.");
    }

    /// <summary>
    /// A claimant who lives to hear the answer carries it, and a public one is a grievance.
    /// </summary>
    /// <remarks>
    /// Driven directly rather than sampled, because a period worth deriving is usually longer than
    /// the rest of a life: across the standard panel every verdict happens to land after its
    /// claimant is dead. That is the more evocative outcome and it is not a reason to leave the
    /// living case unexercised.
    /// </remarks>
    [Fact]
    public void BeingWrongInPublicIsAGrievanceWhenTheClaimantIsAliveToFeelIt()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Standard(11));
        WorldCosmology sky = world.Flavour.Cosmology;
        Civilization civilization = world.Civilizations[EntityId.Civilization(0)];

        List<Apparition> schedule = Skywatch.Apparitions(world);
        Apparition first = schedule[0];
        SystemComet comet = sky.Comets.Single(item => item.Index == first.CometIndex);
        int period = (int)Math.Round(Skywatch.PeriodYears(sky, comet));

        Figure wrong = Scholar(world, civilization, 100, "Bela", first.Year - 30);
        Figure right = Scholar(world, civilization, 101, "Cadia", first.Year - 30);

        // One derives the true period; one doubles it, as a realm with a gap in its register does.
        Claim(wrong, 0, first.Year, period * 2);
        Claim(right, 0, first.Year, period);

        for (int year = first.Year; year <= world.EndYear; year++) SkyClaims.Settle(world, year);

        SkyClaim doubled = Assert.Single(wrong.Claims);
        SkyClaim exact = Assert.Single(right.Claims);

        Assert.Equal(ClaimVerdict.Refuted, doubled.Verdict);
        Assert.Equal(ClaimVerdict.Confirmed, exact.Verdict);
        Assert.True(doubled.ClaimantSawTheAnswer);

        Assert.Contains(wrong.Memories, memory => memory.Kind == MemoryKind.Humiliation);
        Assert.Contains(right.Memories, memory => memory.Kind == MemoryKind.Triumph);

        // The one shown up has a grievance against the one who was right, and it names a year.
        FigureBond bond = Assert.IsType<FigureBond>(LifeStories.BondTo(wrong, right.Id));
        Assert.True(bond.Grievance > 0.0);
        Assert.True(bond.Kinds.HasFlag(BondKind.Rival));
        Assert.Equal(EventKind.SkyClaimRefuted, bond.LastEventKind);

        void Claim(Figure figure, int id, int year, int interval)
        {
            var claim = new SkyClaim(
                id,
                figure.Id,
                civilization.Id,
                comet.Index,
                year,
                ClaimRegister.Measured,
                $"that it returns every {interval} years")
            {
                IntervalYears = interval,
                PredictedYear = year + interval,
                Verdict = ClaimVerdict.Standing,
            };
            claim.RestsOnYears.Add(year);
            figure.Claims.Add(claim);
        }
    }

    /// <summary>Everything a life page needs is in the export.</summary>
    [Fact]
    public void ExportCarriesTheClaimAndItsVerdict()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard(11)).ToExport();
        int seen = 0;

        foreach (ExportFigure figure in export.Figures)
        {
            foreach (ExportSkyClaim claim in figure.Claims)
            {
                Assert.NotEmpty(claim.RestsOnYears);
                Assert.False(string.IsNullOrWhiteSpace(claim.Reading));
                Assert.NotNull(claim.RealmId);

                if (claim.Register == ClaimRegister.Measured)
                {
                    Assert.NotNull(claim.PredictedYear);
                }
                else
                {
                    Assert.Equal(ClaimVerdict.NotTestable, claim.Verdict);
                }

                seen++;
            }
        }

        Assert.True(seen > 0, "Seed 11 exported no claim.");
    }

    private static Figure Scholar(
        WorldState world, Civilization civilization, int id, string name, int birthYear)
    {
        var figure = new Figure(
            EntityId.Figure(id),
            civilization.Id,
            civilization.CultureId,
            name,
            Sex.Female,
            birthYear)
        {
            Occupation = Occupation.Scribe,
            ResidenceSettlementId = civilization.CapitalId,
        };

        world.Figures.Add(figure);
        return figure;
    }
}
