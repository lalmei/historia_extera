using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// Personal quarrels between two named people, from a recorded wrong to how it ended.
/// </summary>
/// <remarks>
/// <para><b>A quarrel is opened by an event, not by a survey.</b> Nothing here looks for two people
/// who dislike each other. The four causes are pushed in at the moment the world writes them — an
/// office taken away, a succession lost, a relative murdered, an accusation laid — so every dispute
/// can name the year and the entity it came from, and no pair can quarrel over having been born in
/// the same realm.</para>
///
/// <para><b>The ladder is public visibility.</b> A grudge is felt, an insult is heard, an accusation
/// is laid before someone who can judge it, and a challenge asks for satisfaction. Climbing is
/// driven by the grievance already in the bond and by both dispositions; a pious, traditional court
/// with a ruler to appeal to pulls the other way, and most quarrels are answered before anybody
/// draws. What escalation cannot do is skip a rung, so a duel always has the record of the year it
/// took to get there.</para>
///
/// <para><b>Power decides what the quarrel can become.</b> A subject with a grievance against their
/// own ruler cannot demand satisfaction from them; that quarrel either cools, is judged, or goes
/// where such things went historically, which is <see cref="Conspiracies"/>. The two systems share
/// the same bonds and grievances and deliberately answer different halves of the same anger.</para>
///
/// <para>Wounds and deaths go through the shared lifecycle: <see cref="LifeStories.Injure"/> and
/// <see cref="Houses.Die"/>. There is no second wound model for duels and no private death path.</para>
/// </remarks>
public static class Disputes
{
    /// <summary>Years without an act after which a quarrel is treated as having gone cold.</summary>
    private const int StaleYears = 12;

    /// <summary>Years after one quarrel ends before the same person may open another.</summary>
    private const int CooldownYears = 8;

    /// <summary>How much of a grievance a quarrel needs before it is worth acting on.</summary>
    private const double GrievanceFloor = 0.30;

    // -----------------------------------------------------------------------
    // Opening
    // -----------------------------------------------------------------------

    /// <summary>
    /// Offers one recorded wrong the chance to become a quarrel the two parties act on.
    /// </summary>
    /// <remarks>
    /// Called from the event that caused it rather than from the annual pass, which is what keeps
    /// the provenance exact and makes the roll independent of every other person in the world: the
    /// fork is the two participants and the year, so adding a courtier elsewhere cannot move it.
    /// </remarks>
    public static void Consider(
        WorldState world,
        Figure aggrieved,
        Figure rival,
        DisputeCause cause,
        EventKind sourceKind,
        EntityId sourceEntity,
        int year)
    {
        if (aggrieved.Id == rival.Id) return;
        if (!aggrieved.IsAlive || !rival.IsAlive) return;
        if (aggrieved.AgeIn(year) < Succession.MajorityAge) return;
        if (rival.AgeIn(year) < Succession.MajorityAge) return;
        if (!InReach(world, aggrieved, rival)) return;
        if (!Available(aggrieved, year) || !Available(rival, year)) return;

        FigureBond? bond = LifeStories.BondTo(aggrieved, rival.Id);
        double grievance = bond?.Grievance ?? 0.0;
        if (grievance < GrievanceFloor) return;

        double appetite = DetMath.Clamp01(
            (grievance * 0.55)
            + (aggrieved.Disposition.Values.Aggression * 0.25)
            + (aggrieved.Disposition.Independence * 0.20));

        if (!Fork(world, aggrieved, rival, year, "open").Chance(0.18 + (0.45 * appetite))) return;

        EntityId place = world.ResidenceOf(aggrieved);
        var dispute = new FigureDispute(
            OpenedBy(aggrieved),
            aggrieved.Id,
            rival.Id,
            year,
            cause,
            sourceKind,
            sourceEntity,
            place);

        dispute.Acts.Add(new DisputeAct(
            year, sourceKind, DisputeStage.Grudge, aggrieved.Id, CauseDetail(cause)));

        aggrieved.Disputes.Add(dispute);
        rival.Disputes.Add(dispute);

        world.Chronicle.Record(
            year,
            EventKind.DisputeOpened,
            aggrieved.Id,
            obj: rival.Id,
            location: place,
            data: Chronicle.Data(("cause", CauseDetail(cause))),
            significance: Significance.Routine);
    }

    // -----------------------------------------------------------------------
    // The yearly pass
    // -----------------------------------------------------------------------

    /// <summary>Carries every open quarrel one year further, or ends it.</summary>
    /// <remarks>
    /// Iterated from the opener's side only, so a quarrel is advanced exactly once however many
    /// lists hold it. Figures are already in id order, and the roll is forked from the pair and the
    /// year rather than drawn from a stream this loop shares, so iteration position decides nothing.
    /// </remarks>
    public static void Tick(WorldState world, int year)
    {
        // Gathered before anything is advanced, because an ending can reach Houses.Die and from
        // there into the systems that open quarrels. Collecting first means a death this year can
        // never mutate a list this loop is standing in.
        var open = new List<FigureDispute>();
        foreach (Figure figure in world.Figures)
        {
            foreach (FigureDispute dispute in figure.Disputes)
            {
                if (!dispute.IsOpen || dispute.OpenerId != figure.Id) continue;
                if (!world.Figures.Contains(dispute.RivalId)) continue;

                open.Add(dispute);
            }
        }

        foreach (FigureDispute dispute in open)
        {
            if (!dispute.IsOpen) continue;

            Advance(
                world,
                world.Figures[dispute.OpenerId],
                world.Figures[dispute.RivalId],
                dispute,
                year);
        }
    }

    /// <summary>
    /// Closes every quarrel a person's death leaves without a second party.
    /// </summary>
    /// <remarks>
    /// Called from the death itself rather than left to next year's pass, so there is no window in
    /// which the export can show a live quarrel with a dead man in it — including the case that
    /// window would otherwise always catch, a death in the final year of a run.
    /// </remarks>
    public static void EndAtDeath(WorldState world, Figure figure, int year)
    {
        foreach (FigureDispute dispute in figure.Disputes)
        {
            if (!dispute.IsOpen) continue;

            Close(
                dispute,
                year,
                DisputeOutcome.Lapsed,
                EventKind.FigureDied,
                "death ended it unanswered");
        }
    }

    private static void Advance(
        WorldState world, Figure opener, Figure rival, FigureDispute dispute, int year)
    {
        if (!opener.IsAlive || !rival.IsAlive)
        {
            Close(dispute, year, DisputeOutcome.Lapsed, EventKind.FigureDied, "death ended it unanswered");
            return;
        }

        if (!InReach(world, opener, rival))
        {
            Close(
                dispute,
                year,
                DisputeOutcome.Lapsed,
                EventKind.JourneyMade,
                "distance ended it unanswered");
            return;
        }

        if (year - dispute.LastActionYear >= StaleYears)
        {
            Close(
                dispute,
                year,
                DisputeOutcome.Lapsed,
                EventKind.DisputeOpened,
                "it went cold with the years");
            return;
        }

        Figure? arbiter = Arbiter(world, opener, rival, year);
        double heat = Heat(world, opener, rival, year);
        double restraint = Restraint(world, opener, rival, arbiter, year);

        // A challenge is the one rung that was issued in order to be answered. Leaving it on the
        // same slow climb as the others produced quarrels that reached the top and then quietly
        // expired there, which is the least believable ending a challenge has.
        double urgency = dispute.Stage == DisputeStage.Challenge ? 0.38 : 0.0;
        double climb = DetMath.Clamp(
            0.10 + urgency + (0.34 * heat) - (0.18 * restraint), 0.02, 0.72);
        if (Fork(world, opener, rival, year, "escalate").Chance(climb))
        {
            Escalate(world, opener, rival, dispute, year);
            return;
        }

        // Withdrawing is harder the further it has been carried: a man who has laid a charge in
        // open court has more to take back than one who has only been cold at table.
        double brake = dispute.Stage switch
        {
            DisputeStage.Grudge => 1.00,
            DisputeStage.Insult => 0.80,
            DisputeStage.Accusation => 0.60,
            _ => 0.45,
        };
        double answer = DetMath.Clamp(
            (0.05 + (0.30 * restraint) - (0.12 * heat)) * brake, 0.01, 0.40);
        if (Fork(world, opener, rival, year, "answer").Chance(answer))
        {
            Resolve(world, opener, rival, dispute, arbiter, year);
        }
    }

    private static void Escalate(
        WorldState world, Figure opener, Figure rival, FigureDispute dispute, int year)
    {
        if (dispute.Stage == DisputeStage.Challenge)
        {
            Meet(world, opener, rival, dispute, year);
            return;
        }

        dispute.Stage++;
        dispute.LastActionYear = year;
        dispute.PlaceId = world.ResidenceOf(opener);
        dispute.Acts.Add(new DisputeAct(
            year, EventKind.DisputeEscalated, dispute.Stage, opener.Id, StageDetail(dispute.Stage)));

        LifeStories.AddRivalry(
            opener,
            rival,
            year,
            EventKind.DisputeEscalated,
            dispute.PlaceId,
            grievance: 0.10,
            sourceEntity: rival.Id);

        if (dispute.Stage == DisputeStage.Accusation)
        {
            rival.AccusedYear = year;
            rival.AccusedOfId = opener.Id;
        }

        world.Chronicle.Record(
            year,
            EventKind.DisputeEscalated,
            opener.Id,
            obj: rival.Id,
            location: dispute.PlaceId,
            data: Chronicle.Data(
                ("act", StageVerb(dispute.Stage)),
                ("actSelf", StageVerbCapitalised(dispute.Stage))),
            significance: Significance.Routine);
    }

    // -----------------------------------------------------------------------
    // Endings
    // -----------------------------------------------------------------------

    private static void Resolve(
        WorldState world,
        Figure opener,
        Figure rival,
        FigureDispute dispute,
        Figure? arbiter,
        int year)
    {
        bool judged = arbiter is not null && dispute.Stage >= DisputeStage.Accusation;
        EntityId place = world.ResidenceOf(opener);

        if (judged)
        {
            dispute.ArbiterId = arbiter!.Id;
            LifeStories.Reconcile(
                opener, rival, year, EventKind.DisputeSettled, place, 0.55, warmly: false);
            Close(
                dispute,
                year,
                DisputeOutcome.Settled,
                EventKind.DisputeSettled,
                "the court judged between them");
        }
        else
        {
            LifeStories.Reconcile(
                opener, rival, year, EventKind.DisputeSettled, place, 0.80, warmly: true);
            Close(
                dispute,
                year,
                DisputeOutcome.Reconciled,
                EventKind.DisputeSettled,
                "they were reconciled");
        }

        dispute.PlaceId = place;
        world.Chronicle.Record(
            year,
            EventKind.DisputeSettled,
            opener.Id,
            obj: rival.Id,
            location: place,
            extra: judged ? new[] { arbiter!.Id } : null,
            data: Chronicle.Data(("manner", dispute.Resolution ?? "settled")),
            significance: Significance.Routine);
    }

    /// <summary>
    /// The meeting a challenge asked for, if the realm allows one.
    /// </summary>
    /// <remarks>
    /// A restrained court is the commonest ending for a quarrel that got this far: someone with
    /// standing forbids the meeting and imposes terms instead. Where nobody does, the two meet, and
    /// which of them walks away is decided by what the record already says about them rather than
    /// by a flat coin.
    /// </remarks>
    private static void Meet(
        WorldState world, Figure opener, Figure rival, FigureDispute dispute, int year)
    {
        Figure? arbiter = Arbiter(world, opener, rival, year);
        double tolerance = DetMath.Clamp01(
            0.45
            + (CivilizationValues(world, opener).Aggression * 0.40)
            - (arbiter is null ? 0.0 : 0.20));

        if (!Fork(world, opener, rival, year, "meet").Chance(tolerance))
        {
            dispute.ArbiterId = arbiter?.Id ?? EntityId.None;
            LifeStories.Reconcile(
                opener,
                rival,
                year,
                EventKind.DisputeSettled,
                world.ResidenceOf(opener),
                0.45,
                warmly: false);
            Close(
                dispute,
                year,
                DisputeOutcome.Settled,
                EventKind.DisputeSettled,
                "the meeting was forbidden them");

            world.Chronicle.Record(
                year,
                EventKind.DisputeSettled,
                opener.Id,
                obj: rival.Id,
                location: dispute.PlaceId,
                extra: arbiter is null ? null : new[] { arbiter.Id },
                data: Chronicle.Data(("manner", "the meeting was forbidden them")),
                significance: Significance.Routine);
            return;
        }

        IRng fate = Fork(world, opener, rival, year, "duel");
        EntityId place = world.ResidenceOf(opener);
        dispute.PlaceId = place;

        double openerEdge = Prowess(opener, year) / (Prowess(opener, year) + Prowess(rival, year));
        bool openerWins = fate.Fork("winner").Chance(openerEdge);
        Figure victor = openerWins ? opener : rival;
        Figure beaten = openerWins ? rival : opener;

        double lethal = DetMath.Clamp(
            0.16
            + (CivilizationValues(world, opener).Aggression * 0.24)
            + (Grievance(opener, rival) * 0.14),
            0.08,
            0.50);

        if (fate.Fork("lethal").Chance(lethal))
        {
            Close(
                dispute,
                year,
                DisputeOutcome.Killed,
                EventKind.DuelFought,
                "one of them was killed",
                act: "killed them");

            world.Chronicle.Record(
                year,
                EventKind.DuelFought,
                victor.Id,
                obj: beaten.Id,
                location: place,
                data: Chronicle.Data(
                    ("cause", CauseDetail(dispute.Cause)),
                    ("result", "killed")));

            LifeStories.Remember(
                victor, MemoryKind.Triumph, year, EventKind.DuelFought, beaten.Id, place, 0.70);
            Houses.Die(
                world,
                beaten,
                year,
                DeathCause.Duel,
                "over " + CauseDetail(dispute.Cause),
                new[] { victor.Id });
            return;
        }

        FigureInjury wound = LifeStories.Injure(
            world,
            beaten,
            victor.Id,
            EventKind.DuelFought,
            place,
            year,
            fate.Fork("wound"),
            record: false);

        Close(
            dispute,
            year,
            DisputeOutcome.Wounded,
            EventKind.DuelFought,
            "one of them was wounded",
            act: "wounded them");

        world.Chronicle.Record(
            year,
            EventKind.DuelFought,
            victor.Id,
            obj: beaten.Id,
            location: place,
            data: Chronicle.Data(
                ("cause", CauseDetail(dispute.Cause)),
                ("result", "wounded"),
                ("injury", wound.Detail)));

        // Blood answers the victor's grievance and creates the loser's. That asymmetry is the
        // point: a quarrel ended this way is over, and the next one has somewhere to start.
        LifeStories.Reconcile(
            opener,
            rival,
            year,
            EventKind.DuelFought,
            place,
            0.35,
            warmly: false);
        LifeStories.Embitter(
            beaten, victor, year, EventKind.DuelFought, victor.Id, place, 0.42, fear: 0.22);
        LifeStories.Remember(
            victor, MemoryKind.Triumph, year, EventKind.DuelFought, beaten.Id, place, 0.55);
    }

    /// <summary>Writes the ending into the shared record both parties read.</summary>
    /// <remarks>
    /// <para>The act's kind is passed rather than derived, because a quarrel that lapsed for a
    /// death is a different fact from one that lapsed for distance, and the life page says
    /// which.</para>
    ///
    /// <para>The last act and the resolution are the same sentence in two grammars. The act is a
    /// line in a list of things that were done — "wounded them" — and the resolution completes
    /// "ended when", which the same words do not fit. Where they do, the caller passes one.</para>
    /// </remarks>
    private static void Close(
        FigureDispute dispute,
        int year,
        DisputeOutcome outcome,
        EventKind actKind,
        string how,
        string? act = null)
    {
        dispute.Outcome = outcome;
        dispute.Resolution = how;
        dispute.EndYear = year;
        dispute.LastActionYear = year;
        dispute.Acts.Add(new DisputeAct(year, actKind, dispute.Stage, dispute.OpenerId, act ?? how));
    }

    // -----------------------------------------------------------------------
    // Reading the world
    // -----------------------------------------------------------------------

    /// <summary>How hard the two are pushing, before anything pushes back.</summary>
    private static double Heat(WorldState world, Figure opener, Figure rival, int year)
    {
        FeelingState feelings = LifeStories.Feelings(opener, year);

        return DetMath.Clamp01(
            (Grievance(opener, rival) * 0.34)
            + (feelings.Anger * 0.22)
            + (opener.Disposition.Values.Aggression * 0.24)
            + (CivilizationValues(world, opener).Aggression * 0.20));
    }

    /// <summary>Everything that pulls a quarrel back down: law, faith, custom and rank.</summary>
    private static double Restraint(
        WorldState world, Figure opener, Figure rival, Figure? arbiter, int year)
    {
        CultureValues values = CivilizationValues(world, opener);
        double restraint = (values.Piety * 0.26) + (values.Tradition * 0.26);

        if (arbiter is not null) restraint += 0.18;

        // Rank is not courage. A man does not demand satisfaction of the person who can hang him,
        // and the anger that has nowhere to go is what the conspiracy model is for.
        restraint += PowerGap(world, opener, rival, year) * 0.30;

        FigureBond? bond = LifeStories.BondTo(opener, rival.Id);
        if (bond is not null)
        {
            restraint += bond.Fear * 0.14;
            if (bond.Kinds.HasFlag(BondKind.Kin)) restraint += 0.12;
        }

        return DetMath.Clamp01(restraint);
    }

    /// <summary>How far above the aggrieved party the other one stands.</summary>
    private static double PowerGap(WorldState world, Figure opener, Figure rival, int year)
    {
        double gap = 0.0;
        if (world.Civilizations.Contains(rival.CivilizationId)
            && world.Civilizations[rival.CivilizationId].CurrentRulerId == rival.Id)
        {
            gap += 0.70;
        }

        bool rivalHolds = rival.Offices.Exists(office => office.ToYear is null);
        bool openerHolds = opener.Offices.Exists(office => office.ToYear is null);
        if (rivalHolds && !openerHolds) gap += 0.30;
        if (openerHolds && !rivalHolds) gap -= 0.15;

        if (opener.AgeIn(year) < 25) gap += 0.10;

        return DetMath.Clamp01(gap);
    }

    /// <summary>Who could impose terms on both of them, if anyone can.</summary>
    private static Figure? Arbiter(WorldState world, Figure opener, Figure rival, int year)
    {
        if (!world.Civilizations.Contains(opener.CivilizationId)) return null;

        Civilization civilization = world.Civilizations[opener.CivilizationId];
        Figure? ruler = world.Figures.Contains(civilization.CurrentRulerId)
            ? world.Figures[civilization.CurrentRulerId]
            : null;
        if (Standing(ruler)) return ruler;

        Figure? priest = Offices.HolderOf(world, civilization, OfficeKind.HighPriest);
        return Standing(priest) ? priest : null;

        bool Standing(Figure? candidate) =>
            candidate is not null
            && candidate.IsAlive
            && candidate.Id != opener.Id
            && candidate.Id != rival.Id
            && candidate.AgeIn(year) >= Succession.MajorityAge;
    }

    /// <summary>What a person brings to a meeting they cannot delegate.</summary>
    private static double Prowess(Figure figure, int year)
    {
        double prowess = 0.35 + (figure.Disposition.Values.Aggression * 0.45);

        if (figure.Offices.Exists(office =>
                office.ToYear is null && office.Kind == OfficeKind.Marshal))
        {
            prowess += 0.30;
        }

        foreach (CampaignMemory memory in figure.Campaigns)
        {
            if (memory.Role is CampaignRole.Fought or CampaignRole.Commanded)
            {
                prowess += 0.05;
                break;
            }
        }

        int age = figure.AgeIn(year);
        if (age > 45) prowess -= Math.Min(0.30, (age - 45) * 0.015);
        if (age < 22) prowess -= 0.08;

        foreach (FigureInjury injury in figure.Injuries)
        {
            if (injury.Permanent) prowess -= 0.12;
            else if (injury.IsRecovering(year)) prowess -= 0.20;
        }

        return Math.Max(0.10, prowess);
    }

    private static double Grievance(Figure figure, Figure other) =>
        LifeStories.BondTo(figure, other.Id)?.Grievance ?? 0.0;

    private static CultureValues CivilizationValues(WorldState world, Figure figure) =>
        world.Civilizations.Contains(figure.CivilizationId)
            ? world.ValuesFor(world.Civilizations[figure.CivilizationId])
            : world.CultureOf(figure).Values;

    /// <summary>
    /// Whether the two are close enough to quarrel at all.
    /// </summary>
    /// <remarks>
    /// A realm, not a settlement. Courts move, and requiring the same town would confine quarrels
    /// to people who happened to share a residence in the year the wrong was done. Crossing a
    /// border, on the other hand, genuinely puts a man out of reach of the one he wronged, which
    /// is why a quarrel can lapse for distance.
    /// </remarks>
    private static bool InReach(WorldState world, Figure first, Figure second) =>
        first.CivilizationId == second.CivilizationId
        && world.Civilizations.Contains(first.CivilizationId);

    /// <summary>One open quarrel at a time, and a breathing space after the last one.</summary>
    private static bool Available(Figure figure, int year)
    {
        foreach (FigureDispute dispute in figure.Disputes)
        {
            if (dispute.IsOpen) return false;
            if (dispute.EndYear is int ended && year - ended < CooldownYears) return false;
        }

        return true;
    }

    private static int OpenedBy(Figure figure)
    {
        int opened = 0;
        foreach (FigureDispute dispute in figure.Disputes)
        {
            if (dispute.OpenerId == figure.Id) opened++;
        }

        return opened;
    }

    /// <summary>
    /// One stream per pair, per year, per question.
    /// </summary>
    /// <remarks>
    /// Keyed on both participants rather than on iteration order or a per-civilization stream, so
    /// that a quarrel between two people resolves the same way whoever else was born that year.
    /// </remarks>
    private static IRng Fork(
        WorldState world, Figure opener, Figure rival, int year, string question) =>
        world.Root
            .Fork("dispute", opener.Id.ToDiscriminator())
            .Fork("with", rival.Id.ToDiscriminator())
            .Fork(question, year);

    private static string CauseDetail(DisputeCause cause) => cause switch
    {
        DisputeCause.OfficeRevoked => "an office taken from them",
        DisputeCause.SuccessionPassedOver => "a succession they lost",
        DisputeCause.KinMurdered => "the murder of their kin",
        _ => "an accusation at court",
    };

    /// <summary>How the act reads on the quarrel's own record, where the parties are already named.</summary>
    private static string StageDetail(DisputeStage stage) => stage switch
    {
        DisputeStage.Insult => "insulted them openly",
        DisputeStage.Accusation => "laid a charge against them",
        DisputeStage.Challenge => "demanded satisfaction of them",
        _ => "held it against them",
    };

    /// <summary>
    /// The same act as a bare verb, for a chronicle line that supplies its own object.
    /// </summary>
    /// <remarks>
    /// Two spellings because a template cannot capitalise. The narration reads "X insulted Y" in
    /// the third person and "Insulted Y" on X's own page, and both need the verb without the
    /// pronoun that the quarrel record can afford to use.
    /// </remarks>
    private static string StageVerb(DisputeStage stage) => stage switch
    {
        DisputeStage.Insult => "insulted",
        DisputeStage.Accusation => "laid a charge against",
        DisputeStage.Challenge => "demanded satisfaction of",
        _ => "held a grudge against",
    };

    private static string StageVerbCapitalised(DisputeStage stage) => stage switch
    {
        DisputeStage.Insult => "Insulted",
        DisputeStage.Accusation => "Laid a charge against",
        DisputeStage.Challenge => "Demanded satisfaction of",
        _ => "Held a grudge against",
    };
}
