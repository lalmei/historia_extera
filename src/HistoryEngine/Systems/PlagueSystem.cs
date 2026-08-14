using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Epidemics: where they start, how far they travel, and what they cost.
/// </summary>
/// <remarks>
/// <para><b>A plague is the first thing in this engine that kills across a whole region at once
/// without anybody deciding to.</b> War is chosen, famine follows a bad harvest that follows the
/// land, and old age comes for one person at a time. Pestilence is the model's only source of
/// sudden, undeserved, correlated loss — a realm can lose a third of three towns and a king in
/// four years having done nothing at all — and that is what it is here for. It is also the one
/// mechanism that reaches the succession machinery sideways: a plague year kills rulers of every
/// age, which is how a throne passes to a child.</para>
///
/// <para><b>Ignition scales with urbanisation.</b> Epidemics need density, so only towns and
/// cities can start one and the world's yearly chance rises with how many of them there are. A
/// world of hamlets in its first century has almost none; the same world at three hundred years
/// has one every few decades. That gets the shape right for free — the plague years arrive once
/// there is something to lose.</para>
///
/// <para><b>Arrival is recorded, presence is not.</b> One event when it reaches a place, with
/// that year's toll, and one when the whole thing burns out. Writing a line for every infected
/// town every year read as a loop rather than as a plague, which is the same lesson the sacking
/// rule learned in M6.</para>
///
/// <para>Samples no terrain: spread is decided by distance between settlements the simulation
/// already knows the coordinates of.</para>
/// </remarks>
public sealed class PlagueSystem : IYearSystem
{
    /// <summary>Yearly ignition chance contributed by each town or city in the world.</summary>
    private const double IgnitionPerUrbanSettlement = 0.0009;

    /// <summary>Ceiling on the yearly chance, however large the world grows.</summary>
    private const double MaxIgnitionChance = 0.030;

    /// <summary>
    /// How many plagues may run at once.
    /// </summary>
    /// <remarks>
    /// Two, so a second can arrive while the first is still burning — which happens — without the
    /// world ever being in a state where every settlement is infected by something and population
    /// never recovers between them.
    /// </remarks>
    private const int MaxConcurrentOutbreaks = 2;

    /// <summary>A plague needs a town to start in. Below this it is an unremarkable bad year.</summary>
    private const int MinimumSeatOfInfection = 900;

    /// <summary>How far a plague can reach in one jump, in world units.</summary>
    private const double SpreadRange = 950.0;

    /// <summary>
    /// How sharply a plague's reach falls off as it spreads.
    /// </summary>
    /// <remarks>
    /// <para>The term that makes an epidemic a regional catastrophe rather than a world-ending
    /// one, and it was not optional. Without it the first cut of this system produced five
    /// pandemics in three centuries that each reached about thirty-four settlements — every
    /// inhabited place in the world, five times over. Eighteen settlements were abandoned against
    /// M7's one and the world ended with five cities instead of fifteen: the flavour milestone had
    /// quietly become the dominant force in the model.</para>
    ///
    /// <para>What was missing is that people react. A plague two towns away closes gates, stops
    /// markets and turns travellers back, so each place it reaches makes the next one harder — and
    /// an epidemic burns out having taken a district rather than a continent. Modelled as decay in
    /// the spread chance against how far it has already got, which is one line and does the whole
    /// job.</para>
    /// </remarks>
    private const double QuarantineDecay = 0.30;

    /// <summary>Yearly chance an infected settlement stops being infected.</summary>
    private const double RecoveryChance = 0.5;

    /// <summary>Extra yearly mortality for figures of a realm with the plague in it.</summary>
    private const double CourtMortality = 0.055;

    /// <summary>Loss below which the arrival is not worth a number in the chronicle.</summary>
    private const int NotableLoss = 15;

    public string Name => "plague";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        // Existing outbreaks are advanced before a new one can ignite, so a plague that starts
        // this year does not also get a year of killing out of the same tick.
        Advance(world, year, rng);
        MaybeIgnite(world, year, rng);
    }

    // -----------------------------------------------------------------------
    // Running outbreaks
    // -----------------------------------------------------------------------

    private static void Advance(WorldState world, int year, IRng rng)
    {
        // Collected first: an outbreak that burns out is removed from the list being walked.
        var finished = new List<Outbreak>();

        foreach (Outbreak outbreak in world.Outbreaks)
        {
            Strike(world, outbreak, year, rng);
            Spread(world, outbreak, year, rng);
            Recover(world, outbreak, rng);
            Cull(world, outbreak, year, rng);

            if (outbreak.IsBurningOut) finished.Add(outbreak);
        }

        foreach (Outbreak outbreak in finished)
        {
            world.Outbreaks.Remove(outbreak);

            var data = Chronicle.Data(
                ("name", outbreak.Name),
                ("dead", outbreak.Dead.ToString(CultureInfo.InvariantCulture)),
                ("reached", outbreak.Reached.ToString(CultureInfo.InvariantCulture)));

            int ran = year - outbreak.StartYear;
            if (ran > 0) data["years"] = Chronicle.Years(ran);

            world.Chronicle.Record(
                year, EventKind.PlagueEnded, outbreak.OriginId, data: data);
        }
    }

    /// <summary>Kills, in every settlement the plague is currently in.</summary>
    private static void Strike(WorldState world, Outbreak outbreak, int year, IRng rng)
    {
        foreach (Infection infection in outbreak.Infected)
        {
            Settlement settlement = world.Settlements[infection.SettlementId];
            if (!settlement.IsActive) continue;

            // The arrival year's toll is written into the arrival event, so it is not counted
            // again here — but every year after it is silent, which is why only the total is
            // reported at the end.
            if (infection.Since == year) continue;

            Kill(world, outbreak, settlement, rng);
        }
    }

    /// <summary>Applies one year of one plague to one settlement, and returns the dead.</summary>
    private static int Kill(WorldState world, Outbreak outbreak, Settlement settlement, IRng rng)
    {
        int before = settlement.Population;
        if (before <= 0) return 0;

        double share = outbreak.Lethality * rng.NextDouble(0.6, 1.35);
        int lost = (int)(before * DetMath.Clamp01(share));
        if (lost <= 0) return 0;

        settlement.Population = Math.Max(0, before - lost);

        Civilization owner = world.Civilizations[settlement.CivilizationId];
        owner.Population = Math.Max(0, owner.Population - lost);

        outbreak.Dead += lost;
        return lost;
    }

    /// <summary>Carries the plague to the next town.</summary>
    private static void Spread(WorldState world, Outbreak outbreak, int year, IRng rng)
    {
        // The sources are fixed before any arrival is added, so a plague cannot chain across five
        // towns within a single year by using its own new infections as fresh sources.
        var sources = new List<EntityId>();
        foreach (Infection infection in outbreak.Infected) sources.Add(infection.SettlementId);

        foreach (EntityId sourceId in sources)
        {
            Settlement source = world.Settlements[sourceId];
            if (!source.IsActive) continue;

            foreach (Settlement candidate in world.Settlements)
            {
                if (!candidate.IsActive || candidate.Id == sourceId) continue;
                if (IsInfected(outbreak, candidate.Id)) continue;

                double distance = DetMath.Distance(source.X, source.Z, candidate.X, candidate.Z);
                if (distance > SpreadRange) continue;

                double nearness = 1.0 - DetMath.InverseLerp(0.0, SpreadRange, distance);
                double wary = 1.0 / (1.0 + (outbreak.Reached * QuarantineDecay));
                double chance = outbreak.Virulence * nearness * nearness * Traffic(candidate) * wary;

                if (!rng.Chance(DetMath.Clamp01(chance))) continue;

                Arrive(world, outbreak, candidate, source, year, rng);
            }
        }
    }

    /// <summary>
    /// How exposed a settlement is to whatever the roads bring.
    /// </summary>
    /// <remarks>
    /// Size and trade, because both are traffic. It is what makes a plague travel along the
    /// prosperous coast rather than radiating evenly across the map, and what makes the places
    /// with the most to lose the likeliest to catch it.
    /// </remarks>
    private static double Traffic(Settlement settlement)
    {
        double size = settlement.Tier switch
        {
            SettlementTier.City => 1.0,
            SettlementTier.Town => 0.78,
            SettlementTier.Village => 0.5,
            _ => 0.28,
        };

        double trade = settlement.Specialization switch
        {
            SettlementSpecialization.Trade => 1.45,
            SettlementSpecialization.Fishing => 1.15,
            SettlementSpecialization.Shrine => 1.25,
            _ => 1.0,
        };

        return size * trade;
    }

    private static void Arrive(
        WorldState world,
        Outbreak outbreak,
        Settlement settlement,
        Settlement from,
        int year,
        IRng rng)
    {
        outbreak.Infected.Add(new Infection(settlement.Id, year));
        outbreak.Reached++;

        int lost = Kill(world, outbreak, settlement, rng);

        var data = Chronicle.Data(("name", outbreak.Name));
        if (lost >= NotableLoss) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            EventKind.PlagueSpread,
            settlement.Id,
            obj: settlement.CivilizationId,
            location: from.Id,
            data: data);
    }

    private static void Recover(WorldState world, Outbreak outbreak, IRng rng)
    {
        var recovered = new List<Infection>();

        foreach (Infection infection in outbreak.Infected)
        {
            Settlement settlement = world.Settlements[infection.SettlementId];

            // A place with nobody left in it has nothing more to give the plague.
            if (!settlement.IsActive || settlement.Population <= 0 || rng.Chance(RecoveryChance))
            {
                recovered.Add(infection);
            }
        }

        foreach (Infection infection in recovered) outbreak.Infected.Remove(infection);
    }

    /// <summary>
    /// Kills at court, in every realm the plague is currently in.
    /// </summary>
    /// <remarks>
    /// Plague does not respect a title, and this is the whole reason it reaches the rest of the
    /// engine: a ruler carried off at forty leaves a succession that the ordinary mortality curve
    /// would have produced a generation later, or not at all.
    /// </remarks>
    private static void Cull(WorldState world, Outbreak outbreak, int year, IRng rng)
    {
        var afflicted = new List<EntityId>();

        foreach (Infection infection in outbreak.Infected)
        {
            EntityId civId = world.Settlements[infection.SettlementId].CivilizationId;
            if (!afflicted.Contains(civId)) afflicted.Add(civId);
        }

        if (afflicted.Count == 0) return;

        double mortality = CourtMortality * outbreak.Lethality * 4.0;

        // Id order, which is birth order, so who dies does not depend on where the plague is.
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (!afflicted.Contains(figure.CivilizationId)) continue;
            if (figure.AgeIn(year) < 0) continue;

            if (rng.Chance(DetMath.Clamp01(mortality))) Houses.Die(world, figure, year, DeathCause.Illness);
        }
    }

    private static bool IsInfected(Outbreak outbreak, EntityId settlementId)
    {
        foreach (Infection infection in outbreak.Infected)
        {
            if (infection.SettlementId == settlementId) return true;
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Ignition
    // -----------------------------------------------------------------------

    private static void MaybeIgnite(WorldState world, int year, IRng rng)
    {
        if (world.Outbreaks.Count >= MaxConcurrentOutbreaks) return;

        var seats = new List<Settlement>();
        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.IsActive && settlement.Population >= MinimumSeatOfInfection)
            {
                seats.Add(settlement);
            }
        }

        if (seats.Count == 0) return;

        double chance = Math.Min(MaxIgnitionChance, IgnitionPerUrbanSettlement * seats.Count);
        if (!rng.Chance(chance)) return;

        // Weighted by traffic, so a plague begins where the ships and pilgrims are.
        double total = 0.0;
        foreach (Settlement seat in seats) total += Traffic(seat);

        double roll = rng.NextDouble(0.0, total);
        Settlement origin = seats[seats.Count - 1];

        foreach (Settlement seat in seats)
        {
            roll -= Traffic(seat);
            if (roll <= 0.0)
            {
                origin = seat;
                break;
            }
        }

        IRng naming = rng.Fork("plague", origin.Id.ToDiscriminator());

        var outbreak = new Outbreak(
            Plagues.Name(naming),
            origin.Id,
            year,
            lethality: naming.NextDouble(0.07, 0.21),
            virulence: naming.NextDouble(0.12, 0.40));

        outbreak.Infected.Add(new Infection(origin.Id, year));
        outbreak.Reached = 1;
        world.Outbreaks.Add(outbreak);

        int lost = Kill(world, outbreak, origin, rng);

        var data = Chronicle.Data(("name", outbreak.Name));
        if (lost >= NotableLoss) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            EventKind.PlagueBegan,
            origin.Id,
            obj: origin.CivilizationId,
            location: origin.RegionId,
            data: data);
    }
}

/// <summary>
/// What a plague was called.
/// </summary>
/// <remarks>
/// Composed in English from what it looked like, not drawn from a culture's naming language.
/// A plague crosses every border it meets, so it is the one thing in the world that no single
/// people gets to name — and "the Speckled Death" is how a chronicle written after the fact
/// refers to it regardless of whose chronicle it is.
/// </remarks>
public static class Plagues
{
    private static readonly string[] Adjectives =
    {
        "Speckled", "Grey", "Red", "Wasting", "Silent", "Bitter", "Coughing", "Black",
        "Summer", "Winter", "Salt", "Weeping",
    };

    private static readonly string[] Nouns =
    {
        "Death", "Fever", "Sickness", "Pestilence", "Mortality", "Plague", "Contagion",
    };

    public static string Name(IRng rng) => rng.Pick(Adjectives) + " " + rng.Pick(Nouns);
}
