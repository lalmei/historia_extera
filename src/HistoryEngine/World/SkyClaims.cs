using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// What people said the lights were, and what the sky made of the saying.
/// </summary>
/// <remarks>
/// <para><b>The world settles it, and nothing else does.</b> A measured claim names a year; when
/// that year arrives the comet has either come back or it has not, and the answer was fixed by the
/// orbit before the claimant's realm existed. No roll decides it, no heuristic grades it, and the
/// claimant's rank, learning and piety buy them nothing. That is the entire difference between this
/// and flavour text about scholarship.</para>
///
/// <para><b>Neither register is a strawman.</b> A mythic reading explains and does not predict,
/// which is the honest thing to do when your register does not go back far enough to count from —
/// and most registers do not. A measured one is checkable and can therefore be wrong. Which a person
/// reaches for comes from what the record already says about them: their learning against their
/// piety, their faith's temper, and whether they had two sightings to put together at all.</para>
///
/// <para><b>Being wrong is the interesting outcome.</b> An observer counts from what their own realm
/// wrote down, so a realm that missed a return derives a doubled period in perfectly good faith. The
/// prediction that follows is honest, evidenced, and wrong, and the sky says so in front of
/// everybody. Seed 11 produces both readings of the same comet.</para>
/// </remarks>
public static class SkyClaims
{
    /// <summary>How far a prediction may miss and still count as having named the year.</summary>
    /// <remarks>
    /// One year either side. Apparitions are rounded to whole years from a fractional period, so a
    /// claimant who derives exactly the right interval can still name the neighbouring year through
    /// no fault of their own; anything wider would start forgiving a genuinely wrong period.
    /// </remarks>
    private const int PredictionSlack = 1;

    // -----------------------------------------------------------------------
    // Making a claim
    // -----------------------------------------------------------------------

    /// <summary>
    /// Offers the person who just wrote a sighting down the chance to say what it was.
    /// </summary>
    /// <remarks>
    /// Called from the observation rather than from an annual sweep, so the evidence a claim rests
    /// on is exactly what its claimant had in front of them, and the roll is forked from the pair of
    /// claimant and comet rather than shared with anything else in the world.
    /// </remarks>
    public static void Consider(
        WorldState world, Figure claimant, SkyObservation seen, int year)
    {
        if (Held(claimant, seen.CometIndex)) return;

        IRng fate = world.Root
            .Fork("sky-claim", claimant.Id.ToDiscriminator())
            .Fork("comet", seen.CometIndex)
            .Fork("year", year);

        Religion? faith = world.Religions.Contains(claimant.ReligionId)
            ? world.Religions[claimant.ReligionId]
            : null;

        // Learning against piety, tempered by how much the faith claims to explain. A person who is
        // both learned and devout can reach for either, which is historically the common case.
        double measured = 0.18
            + (claimant.Disposition.Values.Learning * 0.55)
            - (claimant.Disposition.Values.Piety * 0.22)
            - ((faith?.Character.Zealotry ?? 0.0) * 0.18);

        bool canMeasure = seen.Interval is int interval && interval > 0;
        bool speaks = canMeasure
            ? fate.Fork("speak").Chance(0.55)
            : fate.Fork("speak").Chance(0.30);
        if (!speaks) return;

        bool takesTheInterval = canMeasure && fate.Fork("register").Chance(DetMath.Clamp01(measured));

        var claim = new SkyClaim(
            claimant.Claims.Count,
            claimant.Id,
            seen.RealmId,
            seen.CometIndex,
            year,
            takesTheInterval ? ClaimRegister.Measured : ClaimRegister.Mythic,
            takesTheInterval ? IntervalReading(seen.Interval!.Value) : MythicReading(faith, fate));

        foreach (SkyObservation earlier in claimant.Observations)
        {
            if (earlier.CometIndex == seen.CometIndex) claim.RestsOnYears.Add(earlier.Year);
        }

        if (seen.PriorYear is int prior && !claim.RestsOnYears.Contains(prior))
        {
            claim.RestsOnYears.Insert(0, prior);
        }

        if (takesTheInterval)
        {
            claim.IntervalYears = seen.Interval!.Value;
            claim.PredictedYear = year + claim.IntervalYears;
            claim.Verdict = claim.PredictedYear > world.EndYear
                ? ClaimVerdict.Untested
                : ClaimVerdict.Standing;
        }
        else
        {
            claim.Verdict = ClaimVerdict.NotTestable;
        }

        claimant.Claims.Add(claim);

        var data = new DetMap<string, string>
        {
            ["reading"] = claim.Reading,
        };
        if (claim.PredictedYear is int due)
        {
            data["due"] = due.ToString(CultureInfo.InvariantCulture);
        }

        world.Chronicle.Record(
            year,
            EventKind.SkyClaimMade,
            claimant.Id,
            obj: seen.RealmId,
            location: seen.SettlementId,
            data: data,
            significance: takesTheInterval ? Significance.Notable : Significance.Routine);
    }

    // -----------------------------------------------------------------------
    // Letting the sky answer
    // -----------------------------------------------------------------------

    /// <summary>
    /// Settles every claim the sky answered this year, against the sky and nothing else.
    /// </summary>
    /// <remarks>
    /// <para><b>A claim is refuted by a return it did not predict, not by a missed appointment.</b>
    /// This took a wrong turn first and the wrong turn is worth recording. Checking only "did it come
    /// back in the year you named" makes refutation unreachable: every interval anybody derives is a
    /// whole multiple of the true period, so a man who thinks the comet returns every hundred and
    /// fifty years looks up in the right year and sees it. His prediction is correct. His theory is
    /// not, and what shows it is the return in between — the one his period says cannot happen.</para>
    ///
    /// <para>That is how a period is actually falsified, and it makes the doubled intervals of #146
    /// into the failure mode they deserve to be rather than a curiosity that never costs anyone
    /// anything.</para>
    ///
    /// <para>Deliberately indifferent to whether the claimant lived to hear it. A prediction that
    /// outlives its author and is then vindicated is the best thing this system can produce — it is
    /// what happened to Halley, who died sixteen years before the comet came back — and refusing to
    /// settle it would throw that away to save a line of code.</para>
    /// </remarks>
    public static void Settle(WorldState world, int year)
    {
        WorldCosmology sky = world.Flavour.Cosmology;

        var due = new List<(Figure Claimant, SkyClaim Claim, bool Right)>();
        foreach (Figure figure in world.Figures)
        {
            foreach (SkyClaim claim in figure.Claims)
            {
                if (claim.Verdict != ClaimVerdict.Standing) continue;
                if (claim.PredictedYear is not int predicted) continue;

                SystemComet comet = sky.Comets.Single(item => item.Index == claim.CometIndex);
                bool returnedNow = Skywatch.ReturnsIn(sky, comet, year, world.StartYear, 0);

                // Early: the comet is back before the year this claim allows for, so the period it
                // states is too long and the sky has just said so in public. Strictly after the
                // year the claim was made — the sighting it was derived from cannot refute it.
                if (returnedNow && year > claim.Year && year < predicted - PredictionSlack)
                {
                    due.Add((figure, claim, false));
                    continue;
                }

                if (year != predicted) continue;

                due.Add((
                    figure,
                    claim,
                    Skywatch.ReturnsIn(sky, comet, year, world.StartYear, PredictionSlack)));
            }
        }

        foreach ((Figure claimant, SkyClaim claim, bool right) in due)
        {
            claim.Verdict = right ? ClaimVerdict.Confirmed : ClaimVerdict.Refuted;
            claim.SettledYear = year;
            claim.ClaimantSawTheAnswer = claimant.IsAlive;

            world.Chronicle.Record(
                year,
                right ? EventKind.SkyClaimConfirmed : EventKind.SkyClaimRefuted,
                claimant.Id,
                obj: claim.RealmId,
                data: Chronicle.Data(
                    ("reading", claim.Reading),
                    ("made", claim.Year.ToString(CultureInfo.InvariantCulture)),
                    ("posthumous", claimant.IsAlive ? "false" : "true")),
                significance: Significance.Notable);

            if (!claimant.IsAlive) continue;

            // The realm names the memory rather than the town. A claim is entered in a realm's
            // register and answered in front of it, and a claimant may well have outlived the town
            // they wrote it in — or the realm may have fallen and left them living nowhere at all,
            // which is a state the residence lookup is entitled to return and a memory cannot be
            // built out of.
            EntityId where = world.ResidenceOf(claimant);

            if (right)
            {
                LifeStories.Remember(
                    claimant,
                    MemoryKind.Triumph,
                    year,
                    EventKind.SkyClaimConfirmed,
                    claim.RealmId,
                    where,
                    0.88);
            }
            else
            {
                LifeStories.Remember(
                    claimant,
                    MemoryKind.Humiliation,
                    year,
                    EventKind.SkyClaimRefuted,
                    claim.RealmId,
                    where,
                    0.74);
                Fall(world, claimant, claim, year);
            }
        }
    }

    /// <summary>
    /// A public refutation, in front of whoever said otherwise.
    /// </summary>
    /// <remarks>
    /// <para>Being wrong about the sky is not a private disappointment when somebody else in the
    /// same court held a different reading and has just been proved the better of the two. That is a
    /// grievance with a cause, a year and a person, which is exactly what the quarrel model was
    /// built to take — so this hands it over rather than inventing a second way to fall out.</para>
    ///
    /// <para>The rival need not be vindicated yet, and requiring it was the first mistake here: a
    /// period that is too long is caught out by an early return, which arrives before any rival's
    /// own prediction has come due. Nobody can be shown up by a verdict the world has not delivered.
    /// What they are shown up in front of is the person who disagreed with them, which is who they
    /// have to look at afterwards. A confirmed rival is preferred where one exists.</para>
    /// </remarks>
    private static void Fall(WorldState world, Figure claimant, SkyClaim claim, int year)
    {
        Figure? vindicated = null;
        bool vindicatedIsProven = false;

        foreach (Figure other in world.Figures)
        {
            if (!other.IsAlive || other.Id == claimant.Id) continue;
            if (other.CivilizationId != claimant.CivilizationId) continue;

            foreach (SkyClaim theirs in other.Claims)
            {
                if (theirs.RealmId != claim.RealmId) continue;
                if (theirs.CometIndex != claim.CometIndex) continue;
                if (theirs.Verdict == ClaimVerdict.Refuted) continue;

                // Disagreement is the point: someone holding the same period was wrong together
                // with them and is nobody's rival over it.
                if (theirs.Register == claim.Register
                    && theirs.IntervalYears == claim.IntervalYears)
                {
                    continue;
                }

                bool proven = theirs.Verdict == ClaimVerdict.Confirmed;
                bool better = vindicated is null
                    || (proven && !vindicatedIsProven)
                    || (proven == vindicatedIsProven && other.Id.CompareTo(vindicated.Id) < 0);

                if (!better) continue;

                vindicated = other;
                vindicatedIsProven = proven;
            }
        }

        if (vindicated is null) return;

        LifeStories.AddRivalry(
            claimant,
            vindicated,
            year,
            EventKind.SkyClaimRefuted,
            world.ResidenceOf(claimant),
            grievance: 0.44,
            sourceEntity: vindicated.Id);
        Disputes.Consider(
            world,
            claimant,
            vindicated,
            DisputeCause.Accusation,
            EventKind.SkyClaimRefuted,
            vindicated.Id,
            year);
    }

    // -----------------------------------------------------------------------

    private static bool Held(Figure claimant, int cometIndex)
    {
        foreach (SkyClaim claim in claimant.Claims)
        {
            if (claim.CometIndex == cometIndex) return true;
        }

        return false;
    }

    private static string IntervalReading(int interval) =>
        "that it returns every "
        + interval.ToString(CultureInfo.InvariantCulture)
        + " years";

    /// <summary>
    /// What the faith around them would have said it was.
    /// </summary>
    /// <remarks>
    /// Drawn from the faith's own character rather than from a list of portents, so an animist reads
    /// a spirit where a judgement faith reads a warning, and a realm of no faith at all still has
    /// something to say. Written as a claim about the world, not as a superstition to be corrected:
    /// nobody in this model is told they are backward.
    /// </remarks>
    private static string MythicReading(Religion? faith, IRng rng)
    {
        if (faith is null)
        {
            return rng.Fork("plain").Chance(0.5)
                ? "that it is an old light on an errand of its own"
                : "that it burns for something and no one alive knows what";
        }

        return faith.Character.Deity switch
        {
            DeityStructure.Animistic => "that the light has a spirit, as woods and rivers do",
            DeityStructure.Pantheistic => "that the world turns its face and this is the turning",
            DeityStructure.Monotheistic => faith.Character.Afterlife == Afterlife.Judgement
                ? "that it is a warning hung up where none can miss it"
                : "that it is a sign set in the sky by one hand",
            _ => faith.Character.Afterlife == Afterlife.Ancestral
                ? "that the ancestors ride out, and are counted as they pass"
                : "that the gods quarrel, and this is a torch carried between them",
        };
    }
}
