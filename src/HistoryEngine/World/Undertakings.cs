using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>Turns repeated errands into personal arcs with beginnings, steps and endings.</summary>
public static class Undertakings
{
    public readonly record struct JourneyPlan(
        JourneyKind Kind,
        EntityId DestinationId,
        EntityId ViaId,
        string Purpose);

    /// <summary>Offers the next journey an active arc calls for, if this is the year to attempt it.</summary>
    public static JourneyPlan? NextJourney(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        FigureUndertaking? active = CurrentJourney(figure);
        if (active is null) return null;
        if (!ValidDestination(world, active, home))
        {
            Fail(world, figure, active, year, "its destination had passed away");
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
        if (!rng.Fork("undertaking", active.Id).Chance(chance)) return null;

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
            MemoryKind.Ambition);
    }

    /// <summary>Records the journey as one causal step, then settles the arc if it reached an end.</summary>
    public static void NoteJourney(
        WorldState world, Figure figure, FigureUndertaking undertaking, Journey journey, int year)
    {
        undertaking.Steps.Add(
            new UndertakingStep(
                year,
                journey.Outcome == JourneyOutcome.Returned
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

        // Once the founding goal is complete, later travel maintains the relationship it made.
        // It remains a step in that arc without turning a three-step venture into "17 of 3" or
        // announcing the same undertaking as new every few years.
        if (undertaking.State != UndertakingState.Active) return;

        undertaking.Progress++;

        if (undertaking.Progress < undertaking.RequiredProgress) return;

        Complete(world, figure, undertaking, year);
    }

    /// <summary>A bereavement may become a vow whose pilgrimage is attempted in a later travel year.</summary>
    public static void ConsiderBereavementVow(
        WorldState world, Figure mourner, Figure deceased, int year)
    {
        if (!mourner.IsAlive || mourner.ReligionId.IsNone) return;
        if (mourner.Disposition.Values.Piety < 0.48) return;
        if (CurrentJourney(mourner) is not null) return;

        IRng resolve = world.Root
            .Fork("bereavement-vow", mourner.Id.ToDiscriminator())
            .Fork("for", deceased.Id.ToDiscriminator());
        if (!resolve.Chance(0.18 + (0.28 * mourner.Disposition.Values.Piety))) return;

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
            MemoryKind.Bereavement);
    }

    public static FigureUndertaking? Current(Figure figure) =>
        figure.Undertakings.Find(item => item.State == UndertakingState.Active);

    public static FigureUndertaking? CurrentJourney(Figure figure) =>
        figure.Undertakings.Find(item =>
            item.State == UndertakingState.Active && item.Kind != UndertakingKind.Conspiracy);

    public static FigureUndertaking? CurrentConspiracy(Figure figure) =>
        figure.Undertakings.Find(item =>
            item.State == UndertakingState.Active && item.Kind == UndertakingKind.Conspiracy);

    public static FigureUndertaking BeginConspiracy(
        WorldState world, Figure leader, Figure target, int year, double access)
    {
        FigureUndertaking undertaking = Start(
            world,
            leader,
            UndertakingKind.Conspiracy,
            year,
            "the removal of " + target.FullName,
            target.Id,
            world.ResidenceOf(target),
            EntityId.None,
            3,
            MemoryKind.Rivalry);
        undertaking.Access = DetMath.Clamp01(access);
        undertaking.Secrecy = 0.82;
        undertaking.Steps.Add(
            new UndertakingStep(
                year,
                EventKind.UndertakingStarted,
                world.ResidenceOf(target),
                target.Id,
                "Conceived"));
        return undertaking;
    }

    public static void Complete(
        WorldState world, Figure figure, FigureUndertaking undertaking, int year)
    {
        if (undertaking.State != UndertakingState.Active) return;

        undertaking.State = UndertakingState.Succeeded;
        undertaking.EndYear = year;

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
            significance: undertaking.Kind == UndertakingKind.Conspiracy
                ? Significance.Notable
                : Significance.Routine);
    }

    public static void Fail(
        WorldState world, Figure figure, FigureUndertaking undertaking, int year, string cause)
    {
        if (undertaking.State != UndertakingState.Active) return;

        undertaking.State = UndertakingState.Failed;
        undertaking.EndYear = year;

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
            significance: undertaking.Kind == UndertakingKind.Conspiracy
                ? Significance.Notable
                : Significance.Routine);
    }

    /// <summary>Closes every goal a person's death makes impossible.</summary>
    public static void EndAtDeath(WorldState world, Figure figure, int year)
    {
        foreach (FigureUndertaking undertaking in figure.Undertakings)
        {
            if (undertaking.State != UndertakingState.Active) continue;
            Fail(world, figure, undertaking, year, "their death ended it");
        }
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
        MemoryKind motive)
    {
        var undertaking = new FigureUndertaking(
            figure.Undertakings.Count,
            kind,
            year,
            objective,
            target,
            destination,
            via,
            required,
            motive);
        figure.Undertakings.Add(undertaking);

        world.Chronicle.Record(
            year,
            EventKind.UndertakingStarted,
            figure.Id,
            obj: target,
            location: destination,
            data: Chronicle.Data(("kind", kind.ToString()), ("objective", objective)),
            significance: kind == UndertakingKind.Conspiracy || motive == MemoryKind.Bereavement
                ? Significance.Notable
                : Significance.Routine);

        return undertaking;
    }

    private static FigureUndertaking? Match(Figure figure, Journey journey) =>
        figure.Undertakings.Find(item =>
            (item.State is UndertakingState.Active or UndertakingState.Succeeded)
            && item.Kind == KindOf(journey.Kind)
            && (item.ViaId == journey.ViaId || item.DestinationId == journey.ToSettlementId));

    private static UndertakingKind KindOf(JourneyKind kind) => kind switch
    {
        JourneyKind.Trade => UndertakingKind.TradeVenture,
        JourneyKind.Pilgrimage => UndertakingKind.Pilgrimage,
        JourneyKind.Mission => UndertakingKind.MissionaryCircuit,
        _ => UndertakingKind.Embassy,
    };

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
