using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// People leaving home for a year and coming back: trade, visits, pilgrimage, and clergy on the road.
/// </summary>
/// <remarks>
/// <para>Residence is where they live. A journey is a trip. Changing one would make a merchant
/// vanish from their town every year they used a route, and the disasters that reach a residence
/// would miss them for no reason the chronicle could defend.</para>
///
/// <para>One journey per recorded adult per year, after the trade routes of that year exist so a
/// merchant walks a corridor that is actually open. The chronicle marks the trip Routine: the
/// life page wants the itinerary; the world's spine does not.</para>
/// </remarks>
public sealed class TravelSystem : ISystem
{
    public string Name => "travel";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;
        IRng rng = world.Root.Fork(Name, year);

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (figure.AgeIn(year) < Succession.MajorityAge) continue;

            EntityId home = world.ResidenceOf(figure);
            if (home.IsNone) continue;

            IRng person = rng.Fork("figure", figure.Id.ToDiscriminator());
            Consider(world, figure, home, year, person);
        }
    }

    private static void Consider(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        if (TryTrade(world, figure, home, year, rng)) return;
        if (TryMission(world, figure, home, year, rng)) return;
        if (TryPilgrimage(world, figure, home, year, rng)) return;
        TryVisit(world, figure, home, year, rng);
    }

    private static bool TryTrade(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        if (figure.Occupation != Occupation.Merchant) return false;

        var destinations = new List<TradeRoute>();
        foreach (TradeRoute route in TradeRoutes.From(world, home))
        {
            destinations.Add(route);
        }

        if (destinations.Count == 0) return false;
        if (!rng.Chance(0.28 + (figure.Disposition.Values.Mercantile * 0.22))) return false;

        TradeRoute chosen = rng.Pick(destinations);
        EntityId to = chosen.Other(home);
        if (to.IsNone || to == home) return false;

        Record(world, figure, JourneyKind.Trade, year, home, to, chosen.Id, "on trade");
        return true;
    }

    private static bool TryMission(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        bool clergy = figure.Occupation == Occupation.Clergy || figure.Holds(OfficeKind.HighPriest);
        if (!clergy) return false;
        if (figure.ReligionId.IsNone) return false;
        if (!rng.Chance(0.10 + (figure.Disposition.Values.Piety * 0.12))) return false;

        bool copies = rng.Chance(0.40);
        Settlement? destination = copies
            ? PickScriptorium(world, figure, home, rng)
            : PickCoReligionist(world, figure, home, rng);

        if (destination is null) return false;

        string purpose = copies ? "to request copies" : "to preach";
        Record(
            world, figure, JourneyKind.Mission, year, home, destination.Id,
            destination.ReligionId, purpose);
        return true;
    }

    private static bool TryPilgrimage(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        if (figure.ReligionId.IsNone) return false;
        if (figure.Disposition.Values.Piety < 0.42) return false;
        if (!rng.Chance(0.03 + (figure.Disposition.Values.Piety * 0.08))) return false;

        var sites = new List<HolySite>();
        foreach (HolySite site in world.HolySites)
        {
            if (site.ReligionId != figure.ReligionId) continue;
            if (site.FoundedYear > year) continue;
            if (site.SettlementId.IsNone || site.SettlementId == home) continue;
            if (!world.Settlements.Contains(site.SettlementId)) continue;
            if (!world.Settlements[site.SettlementId].IsActive) continue;

            sites.Add(site);
        }

        if (sites.Count == 0) return false;

        HolySite chosen = rng.Pick(sites);
        Record(
            world, figure, JourneyKind.Pilgrimage, year, home, chosen.SettlementId,
            chosen.Id, "on pilgrimage");
        return true;
    }

    private static bool TryVisit(
        WorldState world, Figure figure, EntityId home, int year, IRng rng)
    {
        if (figure.Occupation is not (Occupation.Court or Occupation.Official)) return false;
        if (!world.Civilizations.Contains(figure.CivilizationId)) return false;

        Civilization realm = world.Civilizations[figure.CivilizationId];
        if (realm.Allies.Count == 0) return false;
        if (!rng.Chance(0.06 + (figure.Disposition.Values.Tradition * 0.04))) return false;

        var hosts = new List<Civilization>();
        foreach (KeyValuePair<EntityId, int> pact in realm.Allies)
        {
            if (!world.Civilizations.Contains(pact.Key)) continue;

            Civilization ally = world.Civilizations[pact.Key];
            if (!ally.IsActive) continue;
            if (Diplomacy.AtWar(world, realm.Id, ally.Id)) continue;
            if (!world.Settlements.Contains(ally.CapitalId)) continue;
            if (!world.Settlements[ally.CapitalId].IsActive) continue;
            if (ally.CapitalId == home) continue;

            hosts.Add(ally);
        }

        if (hosts.Count == 0) return false;

        Civilization host = rng.Pick(hosts);
        Record(
            world, figure, JourneyKind.Visit, year, home, host.CapitalId,
            host.Id, "as a guest");
        return true;
    }

    private static Settlement? PickScriptorium(
        WorldState world, Figure figure, EntityId home, IRng rng)
    {
        var towns = new List<Settlement>();
        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive || settlement.Id == home) continue;
            if (settlement.ReligionId != figure.ReligionId) continue;
            if (Diplomacy.AtWar(world, figure.CivilizationId, settlement.CivilizationId)) continue;
            if (!Tomes.HasScriptorium(world, settlement)) continue;

            towns.Add(settlement);
        }

        return towns.Count == 0 ? PickCoReligionist(world, figure, home, rng) : rng.Pick(towns);
    }

    private static Settlement? PickCoReligionist(
        WorldState world, Figure figure, EntityId home, IRng rng)
    {
        var towns = new List<Settlement>();
        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive || settlement.Id == home) continue;
            if (settlement.ReligionId != figure.ReligionId) continue;
            if (settlement.CivilizationId == figure.CivilizationId) continue;
            if (Diplomacy.AtWar(world, figure.CivilizationId, settlement.CivilizationId)) continue;

            towns.Add(settlement);
        }

        return towns.Count == 0 ? null : rng.Pick(towns);
    }

    private static void Record(
        WorldState world,
        Figure figure,
        JourneyKind kind,
        int year,
        EntityId from,
        EntityId to,
        EntityId via,
        string purpose)
    {
        figure.Journeys.Add(new Journey(kind, year, from, to, via));

        var extra = new List<EntityId> { from };
        if (!via.IsNone) extra.Add(via);

        world.Chronicle.Record(
            year,
            EventKind.JourneyMade,
            figure.Id,
            location: to,
            extra: extra.ToArray(),
            data: Chronicle.Data(("purpose", purpose), ("kind", kind.ToString())),
            significance: Significance.Routine);
    }
}
