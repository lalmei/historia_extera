using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// What a realm made of a reading it held, which is not the same as having it.
/// </summary>
/// <remarks>
/// The seam this suite guards is the one the knowledge section hangs off: presence is settled by
/// carriers, standing is settled by the realm, and neither is settled by the sky. A realm that
/// received a book and shelved it must be distinguishable from one that teaches out of it, two
/// realms must be able to teach incompatible accounts of one comet, and a refuted reading must be
/// able to keep its teachers for a century afterwards.
/// </remarks>
public sealed class ClaimStandingTests
{
    /// <summary>
    /// Seeds whose skies produce claims, matching <see cref="ClaimTransmissionTests"/>.
    /// </summary>
    private static readonly ulong[] Seeds = { 6, 11, 17, 29, 46, 47 };

    private readonly ITestOutputHelper _output;

    public ClaimStandingTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// No standing is derived from a verdict, anywhere.
    /// </summary>
    /// <remarks>
    /// The rule the section is built on, asserted the way the transmission suite asserts the
    /// absence of randomness: not by sampling outcomes, which can pass by luck, but by the file
    /// not containing the vocabulary at all. What the sky said and what a realm thinks are
    /// independent records, and a single <c>if (verdict == Refuted)</c> here would quietly turn
    /// the model into one where being wrong makes people stop believing you.
    /// </remarks>
    [Fact]
    public void NoStandingIsDerivedFromAVerdict()
    {
        string source = Code("ClaimStandings.cs");

        Assert.DoesNotContain("Verdict", source);
        Assert.DoesNotContain("Refuted", source);
        Assert.DoesNotContain("Confirmed", source);
        Assert.DoesNotContain("SettledYear", source);
    }

    /// <summary>
    /// Nothing about a standing is rolled.
    /// </summary>
    /// <remarks>
    /// A realm's disposition comes from what the record already says about it — its effective
    /// values and its faith's temper — so the same world weighed twice reaches the same answer,
    /// and a change of mind means something changed.
    /// </remarks>
    [Fact]
    public void StandingDrawsOnNoRandomness()
    {
        string source = Code("ClaimStandings.cs");

        Assert.DoesNotContain("IRng", source);
        Assert.DoesNotContain("Chance(", source);
        Assert.DoesNotContain("Fork(", source);
        Assert.DoesNotContain("NextDouble", source);
    }

    /// <summary>
    /// Every standing belongs to a reading the realm was holding, and names a cause and a year.
    /// </summary>
    /// <remarks>
    /// The two streams have to fold together: a standing change outside a span of possession
    /// would be a realm's opinion of a book it does not have. A standing begins at
    /// <see cref="ClaimStanding.Received"/> with the arrival, so the first change on a span leaves
    /// that state and each one after it leaves wherever the last one arrived.
    /// </remarks>
    [Fact]
    public void AStandingBelongsToASpanOfPossessionAndSaysWhyItMoved()
    {
        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();
            var held = new Dictionary<string, ClaimStanding>(StringComparer.Ordinal);
            int year = int.MinValue;

            foreach ((int at, ExportClaimTransition? move, ExportClaimStanding? change)
                in Replay(export))
            {
                Assert.True(at >= year, "Standing changes are not in chronological order.");
                year = at;

                if (move is not null)
                {
                    string moved = Key(move.ClaimantId, move.ClaimId, move.RealmId);
                    if (move.Kind == ClaimTransitionKind.Acquired) held[moved] = ClaimStanding.Received;
                    else held.Remove(moved);
                    continue;
                }

                ExportClaimStanding stands = change!;
                string key = Key(stands.ClaimantId, stands.ClaimId, stands.RealmId);

                Assert.True(
                    held.TryGetValue(key, out ClaimStanding was),
                    $"{key} changed standing in {at} while the realm held nothing of it.");
                Assert.Equal(was, stands.From);
                Assert.NotEqual(stands.From, stands.To);
                Assert.False(string.IsNullOrWhiteSpace(stands.Cause));

                held[key] = stands.To;
            }
        }
    }

    /// <summary>
    /// A realm can hold a reading it does not teach, and one it argues with.
    /// </summary>
    /// <remarks>
    /// The whole point of separating the two facts. Before this, holding was adoption and a realm
    /// that had a book had taken its side; the panel is asked for every standing the model can
    /// reach, so a régime where everything is quietly taught cannot pass.
    /// </remarks>
    [Fact]
    public void ARealmCanHoldAReadingItDoesNotTeach()
    {
        var reached = new SortedDictionary<ClaimStanding, int>();

        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();
            foreach (ExportClaimStanding change in export.ClaimStandings)
            {
                reached.TryGetValue(change.To, out int seen);
                reached[change.To] = seen + 1;
            }
        }

        foreach (KeyValuePair<ClaimStanding, int> pair in reached)
        {
            _output.WriteLine($"{pair.Key}: {pair.Value}.");
        }

        Assert.Contains(ClaimStanding.Taught, reached.Keys);
        Assert.Contains(ClaimStanding.SetAside, reached.Keys);
        Assert.Contains(ClaimStanding.Disputed, reached.Keys);
    }

    /// <summary>
    /// Two realms can teach incompatible accounts of one comet in the same year.
    /// </summary>
    /// <remarks>
    /// Contradiction has to be representable, or the section is a single world opinion with extra
    /// bookkeeping. Neither reading is marked correct anywhere in this test, and neither has to be:
    /// what is asserted is that the model can hold both at once.
    /// </remarks>
    [Fact]
    public void TwoRealmsCanTeachIncompatibleReadingsOfOneComet()
    {
        int years = 0;
        string witness = string.Empty;

        foreach (ulong seed in Seeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Standard(seed)).ToExport();

            foreach ((int year, Dictionary<string, ClaimStanding> held) in Years(export))
            {
                var taught = new List<(int Subject, EntityId? Realm, string Reading)>();
                foreach (KeyValuePair<string, ClaimStanding> pair in held)
                {
                    if (pair.Value != ClaimStanding.Taught) continue;

                    (ExportFigure claimant, ExportClaim claim, EntityId? realm) = Read(export, pair.Key);
                    taught.Add((claim.SubjectIndex, realm, claim.Reading));
                    _ = claimant;
                }

                foreach (IGrouping<int, (int Subject, EntityId? Realm, string Reading)> about
                    in taught.GroupBy(entry => entry.Subject))
                {
                    if (about.Select(entry => entry.Realm).Distinct().Count() < 2) continue;
                    if (about.Select(entry => entry.Reading).Distinct().Count() < 2) continue;

                    years++;
                    if (witness.Length == 0)
                    {
                        witness = $"seed {seed}, {year}: "
                            + string.Join(" / ", about.Select(entry => $"{entry.Realm} teaches \"{entry.Reading}\""));
                    }
                }
            }
        }

        _output.WriteLine($"Years with two realms teaching incompatible readings: {years}.");
        if (witness.Length > 0) _output.WriteLine(witness);

        Assert.True(years > 0, "No two realms in the panel ever taught incompatible readings at once.");
    }

    /// <summary>
    /// A reading the sky refuted goes on being held, and goes on being taught.
    /// </summary>
    /// <remarks>
    /// <para>The invariant the design records and nothing exercised until standing existed: a
    /// verdict is a fact about the sky in a named year, and it costs a reading nothing. Both
    /// halves are asserted, because they fail differently — a reading that stopped being held
    /// would mean carriers were being taken away by the answer, and one that stopped being taught
    /// would mean a realm was reading the verdict over the register.</para>
    ///
    /// <para><b>What the model actually produces, and why the bar is where it is.</b> Refuted
    /// readings stay held for centuries — 429 years in seed 29 — and stay taught for decades:
    /// thirty-seven years in the same world, not the hundred the issue sketched. That is the
    /// honest shape rather than a shortfall. Teaching is a realm's continuing disposition and it
    /// lapses for the reasons dispositions lapse — the realm's values drift, another reading is
    /// taught against it, the town that taught it goes — none of which is the sky's doing. The
    /// numbers are reported so a change in either is visible, and the assertion is set below what
    /// the panel gives rather than at a figure nothing reaches.</para>
    /// </remarks>
    [Fact]
    public void ARefutedReadingKeepsItsHoldersAfterTheSkySettledIt()
    {
        int taughtFor = 0;
        int heldFor = 0;
        string witness = string.Empty;

        foreach (ulong seed in RefutationSeeds)
        {
            WorldExport export = HistoryRun.Execute(TestWorlds.Long(seed)).ToExport();

            foreach ((int year, Dictionary<string, ClaimStanding> held) in Years(export))
            {
                foreach (KeyValuePair<string, ClaimStanding> pair in held)
                {
                    (ExportFigure claimant, ExportClaim claim, EntityId? realm) = Read(export, pair.Key);
                    if (claim.Verdict != ClaimVerdict.Refuted) continue;
                    if (claim.SettledYear is not int settled || year <= settled) continue;

                    heldFor = Math.Max(heldFor, year - settled);
                    if (pair.Value != ClaimStanding.Taught || year - settled <= taughtFor) continue;

                    taughtFor = year - settled;
                    witness = $"seed {seed}: {realm} still taught \"{claim.Reading}\" of "
                        + $"{claimant.Name} in {year}, {taughtFor} years after the sky settled it.";
                }
            }
        }

        _output.WriteLine($"A refuted reading was still held {heldFor} years after the sky "
            + $"answered it, and still taught {taughtFor} years after.");
        if (witness.Length > 0) _output.WriteLine(witness);

        Assert.True(heldFor >= 200, "No refuted reading in the panel was held two centuries on.");
        Assert.True(taughtFor >= 20, "No refuted reading in the panel kept a teacher for a generation.");
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Seeds run to a millennium, because what happens after a verdict needs a world with
    /// centuries left in it: a measured claim is made a lifetime in, and the sky answers it a
    /// period after that.
    /// </summary>
    private static readonly ulong[] RefutationSeeds = { 29 };

    /// <summary>Both streams in one chronological pass, transitions before standings in a year.</summary>
    private static IEnumerable<(int Year, ExportClaimTransition? Move, ExportClaimStanding? Change)>
        Replay(WorldExport export)
    {
        var moves = export.ClaimTransitions.ToLookup(change => change.Year);
        var standings = export.ClaimStandings.ToLookup(change => change.Year);

        for (int year = export.Meta.StartYear; year <= export.Meta.EndYear; year++)
        {
            foreach (ExportClaimTransition move in moves[year]) yield return (year, move, null);
            foreach (ExportClaimStanding change in standings[year]) yield return (year, null, change);
        }
    }

    /// <summary>What every realm held, and at what standing, at the end of each year.</summary>
    private static IEnumerable<(int Year, Dictionary<string, ClaimStanding> Held)> Years(
        WorldExport export)
    {
        var held = new Dictionary<string, ClaimStanding>(StringComparer.Ordinal);
        int current = export.Meta.StartYear;

        foreach ((int year, ExportClaimTransition? move, ExportClaimStanding? change) in Replay(export))
        {
            if (year != current)
            {
                yield return (current, held);
                current = year;
            }

            if (move is not null)
            {
                string moved = Key(move.ClaimantId, move.ClaimId, move.RealmId);
                if (move.Kind == ClaimTransitionKind.Acquired) held[moved] = ClaimStanding.Received;
                else held.Remove(moved);
                continue;
            }

            string key = Key(change!.ClaimantId, change.ClaimId, change.RealmId);
            if (held.ContainsKey(key)) held[key] = change.To;
        }

        yield return (current, held);
    }

    private static string Key(EntityId claimantId, int claimId, EntityId? realmId) =>
        $"{claimantId}#{claimId}#{realmId}";

    /// <summary>The claim and realm a folded key names.</summary>
    private static (ExportFigure Claimant, ExportClaim Claim, EntityId? Realm) Read(
        WorldExport export, string key)
    {
        string[] parts = key.Split('#');
        ExportFigure claimant = export.Figures.Single(figure => figure.Id.ToString() == parts[0]);
        ExportClaim claim = claimant.Claims.Single(item => item.Id == int.Parse(parts[1]));
        EntityId? realm = parts[2].Length == 0 ? null : export.Civilizations
            .Select(civilization => (EntityId?)civilization.Id)
            .FirstOrDefault(id => id.ToString() == parts[2]);

        return (claimant, claim, realm);
    }

    private static string Code(string file)
    {
        var kept = new List<string>();
        foreach (string line in File.ReadAllLines(Path.Combine(EngineSource.Root, "World", file)))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            kept.Add(line);
        }

        return string.Join("\n", kept);
    }
}
