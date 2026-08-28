using System.Globalization;
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
            Fail(world, figure, active, year, "its destination was no longer available");
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
            _ => new JourneyPlan(
                JourneyKind.Visit, active.DestinationId, active.ViaId, "on an embassy to"),
        };
    }

    /// <summary>Begins an arc before its first journey is written.</summary>
    public static FigureUndertaking PrepareJourney(
        WorldState world, Figure figure, Journey journey, int year)
    {
        FigureUndertaking? existing = Match(figure, journey);
        if (existing is not null) return existing;

        UndertakingKind kind = journey.Kind switch
        {
            JourneyKind.Trade => UndertakingKind.TradeVenture,
            JourneyKind.Pilgrimage => UndertakingKind.Pilgrimage,
            JourneyKind.Mission => UndertakingKind.MissionaryCircuit,
            _ => UndertakingKind.Embassy,
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
            year + (kind == UndertakingKind.TradeVenture ? 8 : 6));
    }

    /// <summary>Records the journey as one causal step, then settles the arc if it reached an end.</summary>
    public static void NoteJourney(
        WorldState world, Figure figure, FigureUndertaking undertaking, Journey journey, int year)
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

        Complete(world, figure, undertaking, year);
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
        Start(
            world,
            mourner,
            UndertakingKind.Pilgrimage,
            year,
            "a pilgrimage in memory of " + deceased.FullName,
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
        WorldState world, Figure figure, FigureUndertaking undertaking, int year)
    {
        if (undertaking.State != UndertakingState.Active) return;

        undertaking.State = UndertakingState.Succeeded;
        undertaking.EndYear = year;
        undertaking.Outcome = "achieved its objective";

        world.Chronicle.Record(
            year,
            EventKind.UndertakingCompleted,
            figure.Id,
            obj: undertaking.TargetId,
            location: undertaking.DestinationId,
            extra: undertaking.ParticipantIds.Count == 0
                ? null
                : undertaking.ParticipantIds.ToArray(),
            data: Chronicle.Data(
                ("kind", undertaking.Kind.ToString()),
                ("objective", undertaking.Objective),
                ("years", (year - undertaking.StartYear).ToString(CultureInfo.InvariantCulture))),
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
            data: Chronicle.Data(
                ("kind", undertaking.Kind.ToString()),
                ("objective", undertaking.Objective),
                ("cause", cause)),
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
            data: Chronicle.Data(
                ("kind", undertaking.Kind.ToString()),
                ("objective", undertaking.Objective),
                ("cause", cause),
                ("state", "abandoned")),
            significance: Significance.Routine);
    }

    /// <summary>Closes every goal a person's death makes impossible.</summary>
    public static void EndAtDeath(WorldState world, Figure figure, int year)
    {
        foreach (FigureUndertaking undertaking in figure.Undertakings)
        {
            if (undertaking.State != UndertakingState.Active) continue;
            Abandon(world, figure, undertaking, year, "their death ended it");
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
                Fail(world, figure, undertaking, year, "its deadline passed");
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
        OfficeKind? requiredOffice = null)
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

        world.Chronicle.Record(
            year,
            EventKind.UndertakingStarted,
            figure.Id,
            obj: target,
            location: destination,
            data: Chronicle.Data(("kind", kind.ToString()), ("objective", objective)),
            significance: motive == MemoryKind.Bereavement
                ? Significance.Notable
                : Significance.Routine);

        return undertaking;
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
        _ => UndertakingKind.Embassy,
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

    private static string Objective(
        UndertakingKind kind, WorldState world, EntityId destination)
    {
        string place = world.NameOf(destination);
        return kind switch
        {
            UndertakingKind.TradeVenture => "a lasting trade venture with " + place,
            UndertakingKind.Pilgrimage => "a pilgrimage to " + place,
            UndertakingKind.MissionaryCircuit => "a missionary circuit through " + place,
            _ => "an embassy to " + place,
        };
    }
}
