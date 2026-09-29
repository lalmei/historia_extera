using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>Turns repeated errands into personal arcs with beginnings, steps and endings.</summary>
public static class Undertakings
{
    /// <summary>At least the next annual decision; goals cannot end and restart in one tick.</summary>
    public const int CooldownYears = 1;

    /// <summary>One goal at a time. A conspiracy is no longer one of these — see <see cref="Conspiracies"/>.</summary>
    public const int MaxActive = 1;

    public readonly record struct JourneyPlan(
        JourneyKind Kind,
        EntityId DestinationId,
        EntityId ViaId,
        string Purpose);

    /// <summary>Offers the next journey an active arc calls for, if this is the year to attempt it.</summary>
    public static JourneyPlan? NextJourney(
        WorldState world, Figure figure, EntityId home, int year)
    {
        FigureUndertaking? active = CurrentJourney(figure);
        if (active is null) return null;
        if (!ValidDestination(world, active, home))
        {
            Fail(world, figure, active, year, "the way there was closed");
            return null;
        }

        double chance = active.Kind switch
        {
            UndertakingKind.Pilgrimage => 0.52,
            UndertakingKind.TradeVenture => 0.38,
            UndertakingKind.MissionaryCircuit => 0.30,
            UndertakingKind.Embassy => 0.22,
            _ => 0.0,
        };
        IRng attempt = world.Root
            .Fork("undertaking", figure.Id.ToDiscriminator())
            .Fork("goal", active.Id)
            .Fork("year", year);
        if (!attempt.Chance(chance)) return null;

        // Exhaustive on purpose: CurrentJourney above already filtered to IsJourney(item.Kind),
        // so active.Kind can only ever be one of the four cases below. A fifth journey kind
        // reaching this switch is exactly the silent-embassy hazard the objective mapping below
        // also guards against — better a thrown exception naming the gap than a wandering
        // craftsman or a marching soldier quietly narrated as a diplomat.
        return active.Kind switch
        {
            UndertakingKind.Pilgrimage => new JourneyPlan(
                JourneyKind.Pilgrimage, active.DestinationId, active.ViaId, "to fulfil a vow at"),
            UndertakingKind.TradeVenture => new JourneyPlan(
                JourneyKind.Trade, active.DestinationId, active.ViaId, "to establish lasting trade"),
            UndertakingKind.MissionaryCircuit => new JourneyPlan(
                JourneyKind.Mission,
                active.DestinationId,
                active.ViaId,
                active.ViaId.Kind == EntityKind.HolySite
                    ? "to fetch copies from"
                    : "to continue preaching among"),
            // The same wording TryVisit used for the first leg — an envoy does not become a
            // different kind of traveller on the second visit, so the line should not either.
            UndertakingKind.Embassy => new JourneyPlan(
                JourneyKind.Visit, active.DestinationId, active.ViaId, "again as a guest of"),
            _ => throw new InvalidOperationException(
                $"No journey wording exists for an active {active.Kind} undertaking."),
        };
    }

    /// <summary>Begins an arc before its first journey is written.</summary>
    /// <param name="opened">
    /// True when this journey is the one that began the arc. The arc then writes no line of its
    /// own: the journey line says it, because it is the same fact in the same year — see
    /// <see cref="JourneyVoice"/>.
    /// </param>
    public static FigureUndertaking PrepareJourney(
        WorldState world, Figure figure, Journey journey, int year, out bool opened)
    {
        FigureUndertaking? existing = Match(figure, journey);
        opened = existing is null;
        if (existing is not null) return existing;

        // Wandering never reaches here — TravelSystem.Record skips PrepareJourney entirely for
        // it — and a Campaign march is recorded straight to the chronicle without ever calling
        // this method. So the only kind left for the default case is Visit, and it is named
        // rather than caught by a silent `_`, for the reason given on JourneyPlan's switch above.
        UndertakingKind kind = journey.Kind switch
        {
            JourneyKind.Trade => UndertakingKind.TradeVenture,
            JourneyKind.Pilgrimage => UndertakingKind.Pilgrimage,
            JourneyKind.Mission => UndertakingKind.MissionaryCircuit,
            JourneyKind.Visit => UndertakingKind.Embassy,
            _ => throw new InvalidOperationException(
                $"A {journey.Kind} journey does not open an undertaking."),
        };
        int required = kind switch
        {
            UndertakingKind.TradeVenture => 3,
            UndertakingKind.MissionaryCircuit => 2,
            UndertakingKind.Embassy => 2,
            _ => 1,
        };

        string objective = Objective(kind, world, journey.ToSettlementId);
        return Start(
            world,
            figure,
            kind,
            year,
            objective,
            journey.ViaId,
            journey.ToSettlementId,
            journey.ViaId,
            required,
            MemoryKind.Ambition,
            journey.ViaId.IsNone ? journey.ToSettlementId : journey.ViaId,
            EventKind.JourneyMade,
            year + (kind == UndertakingKind.TradeVenture ? 8 : 6),
            record: false);
    }

    /// <summary>
    /// Whether this leg would finish the arc, asked before the journey line is written.
    /// </summary>
    /// <remarks>
    /// The same test <see cref="NoteJourney"/> applies afterwards, less the road: the caller has
    /// already rolled the road and passes whether the traveller came through it.
    /// </remarks>
    public static bool WouldComplete(FigureUndertaking undertaking, bool waylaid) =>
        !waylaid
        && undertaking.State == UndertakingState.Active
        && undertaking.Progress + 1 >= undertaking.RequiredProgress;

    /// <summary>
    /// The voice a journey line takes when it opens or closes an arc, or null for a middle leg.
    /// </summary>
    /// <remarks>
    /// <para><b>One fact, one line.</b> A pilgrimage used to write three lines in the same year —
    /// "undertook a pilgrimage to K", "travelled to K, on pilgrimage to the Sanctuary", "completed
    /// a pilgrimage to K, after 0 years" — and a 400-year world carried twelve thousand of them.
    /// The arc's opening and its ending are what the journey that did them was for, so the journey
    /// line says so, and the arc writes nothing of its own on those legs.</para>
    /// <para>Kind-specific rather than a generic "opened"/"closed", because the words differ: a
    /// pilgrim keeps a vow, a merchant settles a trade and an envoy concludes an embassy, and a
    /// generic line would have to repeat the errand's name to say which.</para>
    /// </remarks>
    public static string? JourneyVoice(FigureUndertaking undertaking, bool opened, bool completes)
    {
        string? stem = undertaking.Kind switch
        {
            UndertakingKind.Pilgrimage => "pilgrimage",
            UndertakingKind.TradeVenture => "trade",
            UndertakingKind.MissionaryCircuit => "mission",
            UndertakingKind.Embassy => "embassy",
            _ => null,
        };
        if (stem is null) return null;

        if (opened && completes) return stem;
        if (opened) return stem + "open";
        if (completes) return stem + "close";
        return null;
    }

    /// <summary>Records the journey as one causal step, then settles the arc if it reached an end.</summary>
    /// <param name="narrated">True when the journey line already said the arc was completed.</param>
    public static void NoteJourney(
        WorldState world,
        Figure figure,
        FigureUndertaking undertaking,
        Journey journey,
        int year,
        bool narrated = false)
    {
        AddStep(
            world,
            undertaking,
            new UndertakingStep(
                year,
                // Staying is a way the trip succeeded, not a way it went wrong: the venture
                // reached its destination and the traveller simply did not come back from it.
                journey.Outcome is JourneyOutcome.Returned or JourneyOutcome.Stayed
                    ? EventKind.JourneyMade
                    : EventKind.JourneyWaylaid,
                journey.ToSettlementId,
                journey.ViaId,
                journey.Outcome.ToString()));

        if (journey.Outcome == JourneyOutcome.Lost)
        {
            Fail(world, figure, undertaking, year, "the traveller did not return");
            return;
        }

        LifeStories.Remember(
            figure,
            MemoryKind.Journey,
            year,
            journey.Outcome == JourneyOutcome.Waylaid
                ? EventKind.JourneyWaylaid
                : EventKind.JourneyMade,
            undertaking.TargetId,
            journey.ToSettlementId,
            journey.Outcome == JourneyOutcome.Waylaid
                ? 0.72
                : 0.42 + (0.10 * Math.Min(
                    undertaking.RequiredProgress, undertaking.Progress + 1)));

        if (journey.Outcome == JourneyOutcome.Waylaid) return;

        // A road loss may have ended the goal through the ordinary death path before this method
        // attaches the final journey step. Do not advance an already terminal arc.
        if (undertaking.State != UndertakingState.Active) return;

        undertaking.Progress++;
        undertaking.LastProgressYear = year;

        if (undertaking.Progress < undertaking.RequiredProgress) return;

        Complete(world, figure, undertaking, year, record: !narrated);
    }

    /// <summary>A bereavement may become a vow whose pilgrimage is attempted in a later travel year.</summary>
    public static void ConsiderBereavementVow(
        WorldState world, Figure mourner, Figure deceased, int year)
    {
        if (!mourner.IsAlive || mourner.ReligionId.IsNone) return;
        if (mourner.Disposition.Values.Piety < 0.48) return;
        if (!CanStart(mourner, year)) return;

        double resolveChance = BereavementVowChance(mourner, deceased, year);
        if (resolveChance <= 0.0) return;

        IRng resolve = world.Root
            .Fork("bereavement-vow", mourner.Id.ToDiscriminator())
            .Fork("for", deceased.Id.ToDiscriminator());
        if (!resolve.Chance(resolveChance)) return;

        var sites = new List<HolySite>();
        EntityId home = world.ResidenceOf(mourner);
        foreach (HolySite site in world.HolySites)
        {
            if (site.ReligionId != mourner.ReligionId || site.FoundedYear > year) continue;
            if (!world.Settlements.Contains(site.SettlementId)) continue;
            if (!world.Settlements[site.SettlementId].IsActive || site.SettlementId == home) continue;
            sites.Add(site);
        }

        if (sites.Count == 0) return;

        HolySite chosen = resolve.Pick(sites);
        // Names the destination the same way Objective(...) does for every other pilgrimage, so
        // this vow gets the same "journey" voice and the same single, un-duplicated mention of
        // the place — see UndertakingData's remarks for why that voice assumes the destination
        // is already in the string.
        Start(
            world,
            mourner,
            UndertakingKind.Pilgrimage,
            year,
            "a pilgrimage to " + world.NameOf(chosen.SettlementId) + " in memory of " + deceased.FullName,
            deceased.Id,
            chosen.SettlementId,
            chosen.Id,
            1,
            MemoryKind.Bereavement,
            deceased.Id,
            EventKind.FigureDied,
            year + 6);
    }

    /// <summary>
    /// Resolve available for a memorial vow. Without an active grief-producing memory there is
    /// no vow; piety controls the same deterministic chance once the experience is present.
    /// </summary>
    internal static double BereavementVowChance(Figure mourner, Figure deceased, int year)
    {
        SalientMemory? memory = mourner.Memories.Find(item =>
            item.Kind == MemoryKind.Bereavement && item.AboutId == deceased.Id);
        if (memory is null || !LifeStories.IsActive(memory, year)) return 0.0;
        if (LifeStories.Feelings(mourner, year).Grief < LifeStories.ActiveMemoryThreshold)
        {
            return 0.0;
        }

        return 0.18 + (0.28 * mourner.Disposition.Values.Piety);
    }

    public static FigureUndertaking? Current(Figure figure) =>
        figure.Undertakings.Find(item => item.State == UndertakingState.Active);

    public static FigureUndertaking? CurrentJourney(Figure figure) =>
        figure.Undertakings.Find(item =>
            item.State == UndertakingState.Active && IsJourney(item.Kind));

    public static void Complete(
        WorldState world, Figure figure, FigureUndertaking undertaking, int year, bool record = true)
    {
        if (undertaking.State != UndertakingState.Active) return;

        undertaking.State = UndertakingState.Succeeded;
        undertaking.EndYear = year;
        undertaking.Outcome = "achieved its objective";

        if (!record) return;

        // Omitted rather than written as "0 years", the convention every other span in the
        // chronicle follows: the template's optional clause then drops instead of reading
        // "completed a pilgrimage, after 0 years".
        var extra = new List<(string, string)>();
        int span = year - undertaking.StartYear;
        if (span > 0) extra.Add(("years", Chronicle.Years(span)));

        world.Chronicle.Record(
            year,
            EventKind.UndertakingCompleted,
            figure.Id,
            obj: undertaking.TargetId,
            location: undertaking.DestinationId,
            extra: undertaking.ParticipantIds.Count == 0
                ? null
                : undertaking.ParticipantIds.ToArray(),
            data: Chronicle.Data(UndertakingData(
                undertaking.Kind,
                undertaking.Objective,
                extra.ToArray())),
            significance: Significance.Routine);
    }

    public static void Fail(
        WorldState world, Figure figure, FigureUndertaking undertaking, int year, string cause)
    {
        if (undertaking.State != UndertakingState.Active) return;

        undertaking.State = UndertakingState.Failed;
        undertaking.EndYear = year;
        undertaking.Outcome = cause;

        world.Chronicle.Record(
            year,
            EventKind.UndertakingFailed,
            figure.Id,
            obj: undertaking.TargetId,
            location: undertaking.DestinationId,
            extra: undertaking.ParticipantIds.Count == 0
                ? null
                : undertaking.ParticipantIds.ToArray(),
            data: Chronicle.Data(UndertakingData(
                undertaking.Kind, undertaking.Objective, ("cause", cause))),
            significance: Significance.Routine);
    }

    public static void Abandon(
        WorldState world, Figure figure, FigureUndertaking undertaking, int year, string cause)
    {
        if (undertaking.State != UndertakingState.Active) return;

        undertaking.State = UndertakingState.Abandoned;
        undertaking.EndYear = year;
        undertaking.Outcome = cause;

        world.Chronicle.Record(
            year,
            EventKind.UndertakingFailed,
            figure.Id,
            obj: undertaking.TargetId,
            location: undertaking.DestinationId,
            extra: undertaking.ParticipantIds.Count == 0
                ? null
                : undertaking.ParticipantIds.ToArray(),
            data: Chronicle.Data(UndertakingData(
                undertaking.Kind,
                undertaking.Objective,
                ("cause", cause),
                ("state", "abandoned"))),
            significance: Significance.Routine);
    }

    /// <summary>Closes every goal a person's death makes impossible.</summary>
    public static void EndAtDeath(WorldState world, Figure figure, int year)
    {
        foreach (FigureUndertaking undertaking in figure.Undertakings)
        {
            if (undertaking.State != UndertakingState.Active) continue;
            Abandon(world, figure, undertaking, year, "death came first");
        }
    }

    /// <summary>Ends a goal that depended on an office at the moment that office is lost.</summary>
    public static void EndAtLossOfOffice(
        WorldState world, Figure figure, OfficeKind office, int year)
    {
        foreach (FigureUndertaking undertaking in figure.Undertakings)
        {
            if (undertaking.State != UndertakingState.Active) continue;
            if (undertaking.RequiredOffice != office) continue;
            Abandon(world, figure, undertaking, year, "the loss of office ended it");
        }
    }

    /// <summary>Settles deadlines independently of travel or battle frequency.</summary>
    public static void Tick(WorldState world, int year)
    {
        foreach (Figure figure in world.Figures)
        {
            foreach (FigureUndertaking undertaking in figure.Undertakings)
            {
                if (undertaking.State != UndertakingState.Active) continue;
                if (year <= undertaking.DeadlineYear) continue;
                Fail(world, figure, undertaking, year, "the years allowed for it ran out");
            }
        }
    }

    /// <summary>Turns a defeat into a bounded martial arc, or advances the one already carried.</summary>
    public static void NoteBattle(
        WorldState world, Figure figure, CampaignMemory memory, Battle battle, int year)
    {
        if (!figure.IsAlive || memory.Fate == CampaignFate.Killed) return;

        EntityId opponent = memory.SideId == battle.AttackerId
            ? battle.DefenderId
            : battle.AttackerId;

        FigureUndertaking? revenge = figure.Undertakings.Find(item =>
            item.State == UndertakingState.Active
            && item.Kind == UndertakingKind.Revenge);
        if (revenge is not null)
        {
            if (revenge.TargetId != opponent) return;

            AddStep(
                world,
                revenge,
                new UndertakingStep(
                    year,
                    EventKind.BattleFought,
                    BattlePlace(battle),
                    battle.Id,
                    memory.Triumphant == true ? "Won the answering battle" : "Was defeated again"));
            revenge.Progress++;
            revenge.LastProgressYear = year;

            if (memory.Triumphant == true)
            {
                Complete(world, figure, revenge, year);
            }
            else
            {
                Fail(world, figure, revenge, year, "another defeat ended the attempt");
            }

            return;
        }

        if (memory.Triumphant != false) return;
        if (memory.Role is not (CampaignRole.Commanded or CampaignRole.Fought)) return;
        if (figure.Disposition.Values.Aggression < 0.42) return;
        if (!CanStart(figure, year)) return;

        IRng resolve = world.Root
            .Fork("undertaking-revenge", battle.Id.ToDiscriminator())
            .Fork("figure", figure.Id.ToDiscriminator());
        double chance = 0.14 + (0.34 * figure.Disposition.Values.Aggression);
        if (!resolve.Chance(chance)) return;

        OfficeKind? requiredOffice = figure.Holds(OfficeKind.Marshal)
            ? OfficeKind.Marshal
            : null;
        EntityId sponsor = EntityId.None;
        if (world.Civilizations.Contains(figure.CivilizationId))
        {
            EntityId ruler = world.Civilizations[figure.CivilizationId].CurrentRulerId;
            if (ruler != figure.Id && world.Figures.Contains(ruler)) sponsor = ruler;
        }

        FigureUndertaking undertaking = Start(
            world,
            figure,
            UndertakingKind.Revenge,
            year,
            "revenge against " + world.NameOf(opponent),
            opponent,
            BattlePlace(battle),
            battle.Id,
            2,
            MemoryKind.Defeat,
            battle.Id,
            EventKind.BattleFought,
            year + 12,
            sponsor,
            requiredOffice);
        undertaking.Progress = 1;
        AddStep(
            world,
            undertaking,
            new UndertakingStep(
                year,
                EventKind.BattleFought,
                BattlePlace(battle),
                battle.Id,
                "Swore to answer the defeat"));
    }

    public static bool CanStart(Figure figure, int year)
    {
        if (Current(figure) is not null) return false;

        int latest = int.MinValue;
        foreach (FigureUndertaking undertaking in figure.Undertakings)
        {
            if (undertaking.EndYear is int ended) latest = Math.Max(latest, ended);
        }

        return latest == int.MinValue || year - latest >= CooldownYears;
    }

    private static FigureUndertaking Start(
        WorldState world,
        Figure figure,
        UndertakingKind kind,
        int year,
        string objective,
        EntityId target,
        EntityId destination,
        EntityId via,
        int required,
        MemoryKind motive,
        EntityId motiveEntity,
        EventKind motiveSource,
        int deadline,
        EntityId sponsor = default,
        OfficeKind? requiredOffice = null,
        bool record = true)
    {
        int active = figure.Undertakings.Count(item => item.State == UndertakingState.Active);
        if (active >= MaxActive)
        {
            throw new InvalidOperationException("Undertaking concurrency limit exceeded.");
        }

        var undertaking = new FigureUndertaking(
            figure.Undertakings.Count,
            kind,
            year,
            objective,
            target,
            destination,
            via,
            required,
            motive,
            motiveEntity,
            motiveSource,
            deadline,
            sponsor,
            requiredOffice);
        figure.Undertakings.Add(undertaking);

        if (!record) return undertaking;

        world.Chronicle.Record(
            year,
            EventKind.UndertakingStarted,
            figure.Id,
            obj: target,
            location: destination,
            data: Chronicle.Data(UndertakingData(kind, objective)),
            significance: motive == MemoryKind.Bereavement
                ? Significance.Notable
                : Significance.Routine);

        return undertaking;
    }

    /// <summary>
    /// The data pairs every Started/Completed/Failed event carries, plus the caller's own extras.
    /// </summary>
    /// <remarks>
    /// The "journey" voice is what keeps a chronicle from naming a destination twice. For the
    /// four journey undertakings, <see cref="Objective"/> already bakes the place into the
    /// objective string — necessarily, since the figure page's open-goal card prints that string
    /// on its own with no <c>{location}</c> slot to fall back on — so the World.self templates
    /// keyed <c>.journey</c> in <see cref="Narration"/> drop their own bracketed
    /// <c>[, bound for {location}]</c> / <c>[ at {location}]</c> clause: the reader is told once,
    /// not twice, which town an embassy or a pilgrimage went to. Revenge carries no such
    /// collision — its objective names an enemy, not a place — so it is left to key nothing and
    /// fall through to the plain templates, where {location} still earns its keep naming the
    /// battlefield.
    /// </remarks>
    private static (string, string)[] UndertakingData(
        UndertakingKind kind, string objective, params (string, string)[] extra)
    {
        var pairs = new List<(string, string)> { ("kind", kind.ToString()), ("objective", objective) };
        if (IsJourney(kind)) pairs.Add((Narration.VoiceDataKey, "journey"));

        // Revenge is had or denied, never "completed"; its own voice lets it say so.
        if (kind == UndertakingKind.Revenge) pairs.Add((Narration.VoiceDataKey, "revenge"));
        pairs.AddRange(extra);
        return pairs.ToArray();
    }

    private static FigureUndertaking? Match(Figure figure, Journey journey) =>
        figure.Undertakings.Find(item =>
            item.State == UndertakingState.Active
            && item.Kind == KindOf(journey.Kind)
            && (item.ViaId == journey.ViaId || item.DestinationId == journey.ToSettlementId));

    private static UndertakingKind KindOf(JourneyKind kind) => kind switch
    {
        JourneyKind.Trade => UndertakingKind.TradeVenture,
        JourneyKind.Pilgrimage => UndertakingKind.Pilgrimage,
        JourneyKind.Mission => UndertakingKind.MissionaryCircuit,
        JourneyKind.Visit => UndertakingKind.Embassy,
        _ => throw new InvalidOperationException(
            $"A {kind} journey does not open an undertaking."),
    };

    private static bool IsJourney(UndertakingKind kind) => kind is
        UndertakingKind.TradeVenture
        or UndertakingKind.Pilgrimage
        or UndertakingKind.MissionaryCircuit
        or UndertakingKind.Embassy;

    /// <summary>Adds one real, chronological step and rejects corrupt causal arcs immediately.</summary>
    public static void AddStep(
        WorldState world, FigureUndertaking undertaking, UndertakingStep step)
    {
        // A journey or battle may kill its actor before the caller can attach that very event as
        // the final step. The same-year terminal cause is valid; later mutation is not.
        if (undertaking.State != UndertakingState.Active
            && undertaking.EndYear != step.Year)
        {
            throw new InvalidOperationException("A terminal undertaking cannot gain steps.");
        }

        if (step.Year < undertaking.StartYear
            || (undertaking.Steps.Count > 0 && step.Year < undertaking.Steps[^1].Year))
        {
            throw new InvalidOperationException("Undertaking steps must be chronological.");
        }

        if (step.PlaceId.IsNone && step.SubjectId.IsNone)
        {
            throw new InvalidOperationException("An undertaking step must reference a real entity.");
        }

        if (step.SourceKind == EventKind.Unknown)
        {
            throw new InvalidOperationException("An undertaking step must name a real event kind.");
        }

        if ((!step.PlaceId.IsNone && !Exists(world, step.PlaceId))
            || (!step.SubjectId.IsNone && !Exists(world, step.SubjectId)))
        {
            throw new InvalidOperationException("An undertaking step references an impossible entity.");
        }

        if (undertaking.Steps.Contains(step))
        {
            throw new InvalidOperationException("An undertaking cannot duplicate a causal step.");
        }

        undertaking.Steps.Add(step);
    }

    private static bool Exists(WorldState world, EntityId id) => id.Kind switch
    {
        EntityKind.Culture => world.Cultures.Contains(id),
        EntityKind.Civilization => world.Civilizations.Contains(id),
        EntityKind.Settlement => world.Settlements.Contains(id),
        EntityKind.Figure => world.Figures.Contains(id),
        EntityKind.Dynasty => world.Dynasties.Contains(id),
        EntityKind.War => world.Wars.Contains(id),
        EntityKind.Battle => world.Battles.Contains(id),
        EntityKind.Region => world.Regions.Contains(id),
        EntityKind.Artifact => world.Artifacts.Contains(id),
        EntityKind.Religion => world.Religions.Contains(id),
        EntityKind.TradeRoute => world.TradeRoutes.Contains(id),
        EntityKind.HolySite => world.HolySites.Contains(id),
        _ => false,
    };

    private static EntityId BattlePlace(Battle battle) =>
        battle.SettlementId.IsNone ? battle.RegionId : battle.SettlementId;

    private static bool ValidDestination(
        WorldState world, FigureUndertaking undertaking, EntityId home) =>
        undertaking.DestinationId != home
        && world.Settlements.Contains(undertaking.DestinationId)
        && world.Settlements[undertaking.DestinationId].IsActive;

    /// <summary>
    /// Names the goal for the four journey undertakings. The destination is baked into this
    /// string rather than left for the chronicle's own <c>{location}</c> slot, because the
    /// objective is also read verbatim off the figure's own page in the viewer — the "Undertaking
    /// · a pilgrimage to Shche" line and the open-goal card both print this text directly, with
    /// no destination link of their own to fall back on. The chronicle templates for
    /// UndertakingStarted / Completed / Failed compensate on their side instead: the "journey"
    /// voice they key off of (see <see cref="Start"/>) drops their own bracketed location, so the
    /// place is still said exactly once in the rendered sentence.
    /// </summary>
    private static string Objective(
        UndertakingKind kind, WorldState world, EntityId destination)
    {
        string place = world.NameOf(destination);
        return kind switch
        {
            UndertakingKind.TradeVenture => "a lasting trade venture with " + place,
            UndertakingKind.Pilgrimage => "a pilgrimage to " + place,
            UndertakingKind.MissionaryCircuit => "a missionary circuit through " + place,
            UndertakingKind.Embassy => "an embassy to " + place,
            _ => throw new InvalidOperationException(
                $"{kind} does not name its objective from a destination."),
        };
    }
}
