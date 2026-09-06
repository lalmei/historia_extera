using HistoryEngine.Entities;
using HistoryEngine.Events;
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
    /// A loss names the carrier that actually went, and the town it went from.
    /// </summary>
    /// <remarks>
    /// The export promises that on a loss the carrier is the last one there was. A realm can keep
    /// a reading while the thing carrying it changes underneath — the author dies and the books
    /// they left take over, or the author simply moves house — so a holding has to be moved onto
    /// what is holding it up now. Without that, a realm loses a reading "with its author" in a
    /// year they had been dead for a century, from a town they left long before, and the chronicle
    /// puts the loss in the wrong place. Only the loss is checked: an acquisition says how the
    /// realm came by it, which is a fact about that year and is not rewritten afterwards.
    /// </remarks>
    [Fact]
    public void ALossNamesTheCarrierThatWentAndTheTownItWentFrom()
    {
        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();

            foreach (ExportClaimTransition change in export.ClaimTransitions)
            {
                if (change.Kind != ClaimTransitionKind.Lost) continue;
                if (change.Carrier != ClaimCarrierKind.Claimant) continue;

                ExportFigure author = export.Figures.Single(figure => figure.Id == change.ClaimantId);

                // The pass compares against the previous year, so a death is seen the year after
                // it happens; anything beyond that is a carrier the record failed to move.
                Assert.True(
                    author.DeathYear is not int died || change.Year <= died + 1,
                    $"{change.ClaimantId} lost a reading from its author in {change.Year}, "
                        + $"who died in {author.DeathYear}.");

                // And it went from wherever they were living by then, not wherever they were
                // standing when the realm first came by it. Read at the year before the loss,
                // because that is the state the pass compared against to notice it.
                ExportResidence? seat = null;
                foreach (ExportResidence residence in author.Residences)
                {
                    if (residence.FromYear < change.Year) seat = residence;
                }

                if (seat is null) continue;
                Assert.Equal(seat.SettlementId, change.SettlementId);
            }
        }
    }

    /// <summary>
    /// A copy does not come through what happens to the town that kept it.
    /// </summary>
    /// <remarks>
    /// Before this, a library was the one thing in a settlement that survived everything: a town
    /// could be sacked, burned and given up and its books came through intact, so the only
    /// recorded way to lose a reading was for its author to die. Every destroyed copy here names
    /// the event that destroyed it, and the year is the year that event happened.
    /// </remarks>
    [Fact]
    public void ACopyDoesNotOutliveWhatHappenedToItsTown()
    {
        int destroyed = 0;
        var causes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();

            foreach (ExportArtifact work in export.Artifacts)
            {
                if (work.TomeContents is not ExportTomeContents contents) continue;

                foreach (ExportTomeCopy copy in contents.Copies)
                {
                    if (copy.LostYear is not int lost) continue;

                    Assert.True(lost >= copy.Year, "A copy was lost before it was made.");
                    Assert.False(string.IsNullOrEmpty(copy.LostCause));

                    causes.TryGetValue(copy.LostCause!, out int seen);
                    causes[copy.LostCause!] = seen + 1;
                    destroyed++;

                    // Nothing here is a hazard of its own: a sack or an abandonment is the town's
                    // event, in the town's year, and the copy is among what the town lost.
                    if (copy.LostCause is not ("in the sack" or "abandoned with the town")) continue;

                    EventKind kind = copy.LostCause == "in the sack"
                        ? EventKind.SettlementSacked
                        : EventKind.SettlementAbandoned;

                    Assert.Contains(
                        export.Events,
                        entry => entry.Kind == kind
                            && entry.Year == lost
                            && entry.Subject == copy.SettlementId);
                }
            }
        }

        _output.WriteLine($"Copies destroyed with their towns: {destroyed}.");
        foreach (KeyValuePair<string, int> cause in causes)
        {
            _output.WriteLine($"  {cause.Key}: {cause.Value}.");
        }

        Assert.True(destroyed > 0, "No copy in the panel was destroyed by anything.");
    }

    /// <summary>
    /// A copy that is gone stops carrying, and stays in the record that says it once did not.
    /// </summary>
    /// <remarks>
    /// The two halves of the same promise. A realm cannot go on holding a reading on a copy that
    /// burned twenty years ago, and a reader replaying the world to a year before the fire has to
    /// find the copy still there — which is why a destroyed copy is dated rather than removed.
    /// </remarks>
    [Fact]
    public void ADestroyedCopyStopsCarryingAndStaysInTheRecord()
    {
        int burnt = 0;

        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();

            foreach (ExportClaimTransition change in export.ClaimTransitions)
            {
                if (change.Carrier != ClaimCarrierKind.Text) continue;

                ExportArtifact work = export.Artifacts.Single(item => item.Id == change.CarrierId);
                ExportTomeCopy? copy = null;
                foreach (ExportTomeCopy candidate in work.TomeContents!.Copies)
                {
                    if (candidate.SettlementId == change.SettlementId) copy = candidate;
                }

                if (copy is null) continue;

                if (change.Kind == ClaimTransitionKind.Acquired)
                {
                    Assert.True(
                        copy.LostYear is not int burned || burned > change.Year,
                        $"A reading arrived on a copy lost in {copy.LostYear}.");
                    continue;
                }

                // A loss seated on a copy that was still standing that year is a loss for some
                // other reason — the town left the realm, or was given up — and is not this
                // rule's business. That stays true when the copy burns later: a fire in 225 did
                // not take a reading away in 185, and reading the copy's fate rather than its
                // fate *by then* made this rule fail on the first world that produced the pair.
                if (copy.LostYear is int gone && gone <= change.Year) burnt++;

                // What every loss can be held to, whatever caused it: it is seated on a copy
                // that had already been made. A realm cannot lose a reading on a book that does
                // not exist yet.
                Assert.True(
                    copy.Year <= change.Year,
                    $"A reading was lost in {change.Year} on a copy made in {copy.Year}.");
            }
        }

        // And the burning does take readings away, or the narrowing above would have quietly
        // turned this half of the rule into a rule about nothing.
        Assert.True(burnt > 0, "No reading in the panel was lost on a copy that had burned.");
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
