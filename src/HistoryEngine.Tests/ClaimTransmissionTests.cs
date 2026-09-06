using HistoryEngine.Entities;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// How a reading reaches a realm its author never saw, and how it stops being held.
/// </summary>
public sealed class ClaimTransmissionTests
{
    /// <summary>
    /// Seeds whose skies produce claims at all. Transmission is scarce by construction — a copy is
    /// expensive — so the panel is chosen on claims and the text-carried cases are counted rather
    /// than required of every world.
    /// </summary>
    private static readonly ulong[] Seeds = { 6, 11, 17, 29, 46, 47 };

    private readonly ITestOutputHelper _output;

    public ClaimTransmissionTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// No realm holds a claim that nothing carried to it.
    /// </summary>
    /// <remarks>
    /// The design rule, asserted rather than trusted: every acquisition names a carrier that
    /// exists, and a text-carried one names the settlement the copy sits in. A transition without
    /// one would be knowledge teleporting, which is the failure the whole model is shaped to
    /// prevent.
    /// </remarks>
    [Fact]
    public void EveryTransitionNamesTheCarrierThatExplainsIt()
    {
        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();

            foreach (ExportClaimTransition change in export.ClaimTransitions)
            {
                Assert.NotNull(change.RealmId);
                Assert.NotNull(change.CarrierId);

                ExportFigure claimant = export.Figures.Single(figure => figure.Id == change.ClaimantId);
                ExportClaim claim = claimant.Claims.Single(item => item.Id == change.ClaimId);
                Assert.True(change.Year >= claim.Year);

                if (change.Carrier == ClaimCarrierKind.Claimant)
                {
                    Assert.Equal(claimant.Id, change.CarrierId);
                    continue;
                }

                // A text carrier is a written work that actually says so, in a town it sits in.
                ExportArtifact work = export.Artifacts.Single(item => item.Id == change.CarrierId);
                Assert.NotNull(work.TomeContents);
                Assert.Contains(
                    work.TomeContents!.Carries,
                    carried => carried.ClaimantId == change.ClaimantId
                        && carried.ClaimId == change.ClaimId);
                Assert.NotNull(change.SettlementId);
            }
        }
    }

    /// <summary>
    /// A realm cannot come by the same claim twice without losing it in between, and cannot lose
    /// one it never had.
    /// </summary>
    /// <remarks>
    /// This is what makes the transitions foldable: a viewer replaying them to a year needs the
    /// stream to alternate, exactly as territory transfers do. A duplicate acquisition would leave
    /// a reader unable to say whether a realm holds one thing or two.
    /// </remarks>
    [Fact]
    public void TransitionsAlternateSoAYearCanBeFoldedOutOfThem()
    {
        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();
            var held = new HashSet<string>();
            int year = 0;

            foreach (ExportClaimTransition change in export.ClaimTransitions)
            {
                Assert.True(change.Year >= year, "Transitions are not in chronological order.");
                year = change.Year;

                string key = $"{change.ClaimantId}:{change.ClaimId}:{change.RealmId}";
                if (change.Kind == ClaimTransitionKind.Acquired)
                {
                    Assert.True(held.Add(key), $"{key} was acquired twice without being lost.");
                }
                else
                {
                    Assert.True(held.Remove(key), $"{key} was lost without being held.");
                }
            }
        }
    }

    /// <summary>
    /// Provenance walks back from a holder to the person who first said it.
    /// </summary>
    /// <remarks>
    /// The distinctive question this record exists to answer is not what a realm knew but how it
    /// came to know it. Every text-carried holding must resolve: this realm has it because this
    /// work carries it, written by somebody, from a claim that rests on sightings.
    /// </remarks>
    [Fact]
    public void ATextCarriedHoldingWalksBackToTheClaimAndItsEvidence()
    {
        int walked = 0;

        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();

            foreach (ExportClaimTransition change in export.ClaimTransitions)
            {
                if (change.Kind != ClaimTransitionKind.Acquired) continue;
                if (change.Carrier != ClaimCarrierKind.Text) continue;

                ExportArtifact work = export.Artifacts.Single(item => item.Id == change.CarrierId);
                ExportFigure claimant = export.Figures.Single(figure => figure.Id == change.ClaimantId);
                ExportClaim claim = claimant.Claims.Single(item => item.Id == change.ClaimId);

                Assert.NotEmpty(claim.RestsOnYears);
                Assert.True(work.CreatedYear <= change.Year);
                walked++;
            }
        }

        _output.WriteLine($"Text-carried holdings walked back to their claim: {walked}.");
        Assert.True(walked > 0, "No claim in the panel was ever carried by a written work.");
    }

    /// <summary>
    /// A reading can outlive the person who made it, and cross a border doing it.
    /// </summary>
    /// <remarks>
    /// The point of writing anything down. Reported rather than demanded of every seed: books that
    /// carry a reading are scarce and their copies scarcer, and a panel that required one in every
    /// world would be measuring the wrong thing.
    /// </remarks>
    [Fact]
    public void AReadingCanOutliveItsAuthorAndLeaveItsRealm()
    {
        int crossed = 0;
        int posthumous = 0;

        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();

            foreach (ExportClaimTransition change in export.ClaimTransitions)
            {
                if (change.Kind != ClaimTransitionKind.Acquired) continue;
                if (change.Carrier != ClaimCarrierKind.Text) continue;

                ExportFigure claimant = export.Figures.Single(figure => figure.Id == change.ClaimantId);
                ExportClaim claim = claimant.Claims.Single(item => item.Id == change.ClaimId);

                if (claim.RealmId != change.RealmId) crossed++;
                if (claimant.DeathYear is int died && change.Year > died) posthumous++;
            }
        }

        _output.WriteLine($"Carried across a border: {crossed}. Carried after the author died: {posthumous}.");
        Assert.True(crossed > 0, "No claim in the panel ever left the realm it was made in.");
        Assert.True(posthumous > 0, "No claim in the panel ever outlived its author.");
    }

    /// <summary>
    /// Nothing about transmission is rolled.
    /// </summary>
    /// <remarks>
    /// A claim moves because a copy the circulation model already decided to make arrived, and
    /// stops because carriers stopped surviving. Neither reads a random stream, so running the
    /// pass twice over the same world must produce the same transitions — and, more usefully, the
    /// file itself must contain no source of randomness to drift.
    /// </remarks>
    [Fact]
    public void TransmissionDrawsOnNoRandomness()
    {
        var code = new List<string>();
        foreach (string line in File.ReadAllLines(
            Path.Combine(EngineSource.Root, "World", "ClaimTransmission.cs")))
        {
            // The prose says "IRng" on purpose, to explain why there is not one.
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("///", StringComparison.Ordinal)
                || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            code.Add(line);
        }

        string source = string.Join("\n", code);

        Assert.DoesNotContain("IRng", source);
        Assert.DoesNotContain("Chance(", source);
        Assert.DoesNotContain("Fork(", source);
        Assert.DoesNotContain("NextDouble", source);
    }
}
