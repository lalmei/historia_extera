using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Faiths: where they are first preached, how they travel, and how they break.
/// </summary>
/// <remarks>
/// <para><b>Faith is held by settlements, not by realms.</b> A state religion would be one field
/// and a decree, and nothing would happen between decrees. Holding it per settlement means a
/// faith crosses a border before it crosses a throne — the interesting order — and a realm can be
/// religiously divided, which is what gives a schism somewhere to start. The realm's own faith
/// falls out of it: whatever its capital follows, synced here once a year so everything
/// downstream sees one answer.</para>
///
/// <para><b>Pull is aggregated by region rather than measured settlement to settlement.</b> The
/// honest version — every settlement against every other — is a quadratic scan run every year of
/// the run, and it buys nothing: what a town is exposed to is the faith of the district it sits
/// in and the districts next to it, which the region grid already describes. One pass to build
/// the presence map, one to convert, and the cost stays linear in a world that reaches a couple
/// of hundred settlements.</para>
///
/// <para><b>Tradition is the brake.</b> Without one, faiths chase each other across the map for
/// centuries and the chronicle fills with conversions that mean nothing. A settlement resists in
/// proportion to its culture's tradition and to how long it has held what it holds, so an ancient
/// holy city is nearly immovable while a young frontier village turns twice in a generation.</para>
///
/// <para>Samples no terrain.</para>
/// </remarks>
public sealed class ReligionSystem : IYearSystem
{
    /// <summary>Yearly chance a settlement with no faith at all founds one.</summary>
    private const double FoundingChance = 0.0016;

    /// <summary>A faith needs a congregation before anyone bothers to leave it.</summary>
    private const int SchismMinimumFollowing = 6;

    /// <summary>Yearly chance a large faith splits, per settlement holding it.</summary>
    private const double SchismChance = 0.0012;

    /// <summary>Yearly conversion chance at full pull, before piety, fervour and tradition.</summary>
    private const double ConversionChance = 0.16;

    /// <summary>How much of a neighbouring district's faith is felt across the border.</summary>
    private const double AdjacentWeight = 0.55;

    /// <summary>Years of holding a faith after which a settlement is as settled as it gets.</summary>
    private const double EntrenchmentYears = 120.0;

    public string Name => "religion";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        List<DetMap<EntityId, double>> presence = Presence(world);

        Convert(world, presence, year, rng);
        Found(world, year, rng);
        Schism(world, year, rng);
        Fade(world, year);
        SyncStateFaiths(world, year);
    }

    // -----------------------------------------------------------------------
    // Where each faith is felt
    // -----------------------------------------------------------------------

    /// <summary>
    /// How strongly each faith is present in each region, indexed by region.
    /// </summary>
    /// <remarks>
    /// Weighted by settlement size, because a city's faith is the one a traveller meets. Built
    /// once per year and read by every settlement, which is what keeps this linear.
    /// </remarks>
    private static List<DetMap<EntityId, double>> Presence(WorldState world)
    {
        var presence = new List<DetMap<EntityId, double>>(world.Regions.Count);
        for (int i = 0; i < world.Regions.Count; i++) presence.Add(new DetMap<EntityId, double>());

        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive || settlement.ReligionId.IsNone) continue;

            DetMap<EntityId, double> here = presence[settlement.RegionId.Index];
            here[settlement.ReligionId] =
                here.GetOrDefault(settlement.ReligionId, 0.0) + Weight(settlement);
        }

        return presence;
    }

    private static double Weight(Settlement settlement) => settlement.Tier switch
    {
        SettlementTier.City => 1.0,
        SettlementTier.Town => 0.7,
        SettlementTier.Village => 0.42,
        _ => 0.22,
    };

    // -----------------------------------------------------------------------
    // Conversion
    // -----------------------------------------------------------------------

    private static void Convert(
        WorldState world, List<DetMap<EntityId, double>> presence, int year, IRng rng)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                EntityId chosen = Strongest(world, presence, settlement, civilization, out double pull);
                if (chosen.IsNone || chosen == settlement.ReligionId) continue;

                Religion faith = world.Religions[chosen];
                if (!faith.IsActive) continue;

                double chance = ConversionChance
                                * DetMath.Clamp01(pull)
                                * (0.35 + (culture.Values.Piety * 0.9))
                                * (0.45 + (faith.Fervour * 0.85))
                                * Resistance(culture, settlement, year);

                if (!rng.Chance(DetMath.Clamp01(chance))) continue;

                Adopt(world, settlement, faith, year);
            }
        }
    }

    /// <summary>
    /// How readily a settlement gives up what it already believes, in [0, 1].
    /// </summary>
    /// <remarks>
    /// A place with no faith at all takes one readily. A place that has held one for a century
    /// under a traditional culture is close to immovable — which is what makes an old holy city
    /// stay the thing it is while the frontier changes hands around it.
    /// </remarks>
    private static double Resistance(Culture culture, Settlement settlement, int year)
    {
        if (settlement.ReligionId.IsNone) return 1.0;

        double held = DetMath.InverseLerp(0.0, EntrenchmentYears, year - (settlement.ConvertedYear ?? year));
        double entrenched = 0.55 + (held * 0.35) + (culture.Values.Tradition * 0.45);

        return DetMath.Clamp01(1.0 - DetMath.Clamp01(entrenched));
    }

    /// <summary>The faith pressing hardest on one settlement, and how hard.</summary>
    private static EntityId Strongest(
        WorldState world,
        List<DetMap<EntityId, double>> presence,
        Settlement settlement,
        Civilization civilization,
        out double pull)
    {
        var felt = new DetMap<EntityId, double>();

        Accumulate(felt, presence[settlement.RegionId.Index], 1.0);

        foreach (EntityId neighbour in world.Regions[settlement.RegionId].AdjacentRegions)
        {
            Accumulate(felt, presence[neighbour.Index], AdjacentWeight);
        }

        // A realm's own faith carries the weight of its court, which is how a conquered province
        // eventually comes round to the faith of the realm that took it.
        if (!civilization.StateReligionId.IsNone)
        {
            felt[civilization.StateReligionId] =
                felt.GetOrDefault(civilization.StateReligionId, 0.0) + 0.8;
        }

        // A relic kept here argues for its own faith, which is what relics are for.
        foreach (Artifact artifact in world.Artifacts)
        {
            if (!artifact.IsExtant || artifact.HolderId != settlement.Id) continue;
            if (artifact.ReligionId.IsNone) continue;

            felt[artifact.ReligionId] = felt.GetOrDefault(artifact.ReligionId, 0.0) + 0.6;
        }

        EntityId best = EntityId.None;
        pull = 0.0;

        // DetMap iterates in key order, so the fixed id order breaks exact ties.
        foreach (KeyValuePair<EntityId, double> entry in felt)
        {
            if (entry.Value > pull)
            {
                pull = entry.Value;
                best = entry.Key;
            }
        }

        // Normalised against what a settlement's own district alone would exert, so "pull" means
        // "more than the neighbours already believe" rather than an unbounded sum.
        pull = DetMath.Clamp01(pull / 2.2);
        return best;
    }

    private static void Accumulate(
        DetMap<EntityId, double> into, DetMap<EntityId, double> from, double weight)
    {
        foreach (KeyValuePair<EntityId, double> entry in from)
        {
            into[entry.Key] = into.GetOrDefault(entry.Key, 0.0) + (entry.Value * weight);
        }
    }

    private static void Adopt(WorldState world, Settlement settlement, Religion faith, int year)
    {
        if (!settlement.ReligionId.IsNone && world.Religions.Contains(settlement.ReligionId))
        {
            world.Religions[settlement.ReligionId].Lose(settlement.Id);
        }

        settlement.ReligionId = faith.Id;
        settlement.ConvertedYear = year;
        faith.Gain(settlement.Id);

        world.Chronicle.Record(
            year,
            EventKind.ReligionAdopted,
            settlement.Id,
            obj: faith.Id,
            location: settlement.CivilizationId);
    }

    // -----------------------------------------------------------------------
    // Founding and schism
    // -----------------------------------------------------------------------

    private static void Found(WorldState world, int year, IRng rng)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                if (!settlement.ReligionId.IsNone) continue;
                if (settlement.Tier < SettlementTier.Village) continue;

                // A pious people preaching at a shrine is where a faith starts, but nothing here
                // requires either — a village with an idea is enough, just far less often.
                double chance = FoundingChance
                                * (0.3 + (culture.Values.Piety * 1.8))
                                * (settlement.Specialization == SettlementSpecialization.Shrine ? 3.0 : 1.0);

                if (!rng.Chance(chance)) continue;

                EntityId founderId = Preacher(world, civilization, year);
                Religion faith = Establish(world, settlement, culture, founderId, EntityId.None, year, rng);

                world.Chronicle.Record(
                    year,
                    EventKind.ReligionFounded,
                    faith.Id,
                    obj: founderId,
                    location: settlement.Id);

                Adopt(world, settlement, faith, year);
            }
        }
    }

    private static void Schism(WorldState world, int year, IRng rng)
    {
        // Collected first: founding a splinter appends to the table being walked.
        var splitting = new List<Settlement>();

        foreach (Religion faith in world.ActiveReligions())
        {
            if (faith.SettlementIds.Count < SchismMinimumFollowing) continue;

            foreach (EntityId settlementId in faith.SettlementIds)
            {
                Settlement settlement = world.Settlements[settlementId];
                if (!settlement.IsActive) continue;

                // Never where it began. A faith does not break from itself at its own holy site,
                // and the far end of a congregation is where the argument actually starts.
                if (settlement.Id == faith.OriginSettlementId) continue;
                if (settlement.Tier < SettlementTier.Town) continue;

                if (rng.Chance(SchismChance)) splitting.Add(settlement);
            }
        }

        foreach (Settlement settlement in splitting)
        {
            Religion parent = world.Religions[settlement.ReligionId];
            Civilization civilization = world.Civilizations[settlement.CivilizationId];
            Culture culture = world.CultureOf(civilization);

            Religion splinter = Establish(
                world, settlement, culture, Preacher(world, civilization, year), parent.Id, year, rng);

            world.Chronicle.Record(
                year,
                EventKind.ReligionSchism,
                splinter.Id,
                obj: parent.Id,
                location: settlement.Id);

            Adopt(world, settlement, splinter, year);
        }
    }

    private static Religion Establish(
        WorldState world,
        Settlement settlement,
        Culture culture,
        EntityId founderId,
        EntityId parentId,
        int year,
        IRng rng)
    {
        EntityId id = world.Religions.NextId;

        // Forked on the faith's own id, so its fervour does not depend on how many faiths were
        // founded before it.
        IRng own = rng.Fork("faith", id.ToDiscriminator());

        var faith = new Religion(
            id,
            world.Names.ForReligion(id, culture),
            culture.Id,
            settlement.Id,
            year,
            fervour: DetMath.Clamp01(own.NextDouble(0.15, 0.85) + (culture.Values.Piety * 0.2)))
        {
            FounderId = founderId,
            ParentId = parentId,
        };

        world.Religions.Add(faith);
        return faith;
    }

    /// <summary>
    /// Whoever is remembered as having preached it first.
    /// </summary>
    /// <remarks>
    /// The oldest living adult of the realm who is not on its throne. A ruler founding the faith
    /// reads as policy rather than revelation, and the point of naming a founder at all is that a
    /// faith is started by a person the chronicle can then follow to their death.
    /// </remarks>
    private static EntityId Preacher(WorldState world, Civilization civilization, int year)
    {
        EntityId eldest = EntityId.None;
        int oldest = -1;

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != civilization.Id) continue;
            if (figure.Id == civilization.CurrentRulerId) continue;

            int age = figure.AgeIn(year);
            if (age < 20 || age <= oldest) continue;

            oldest = age;
            eldest = figure.Id;
        }

        return eldest;
    }

    // -----------------------------------------------------------------------
    // Endings and state faith
    // -----------------------------------------------------------------------

    private static void Fade(WorldState world, int year)
    {
        foreach (Religion faith in world.Religions)
        {
            if (!faith.IsActive || faith.SettlementIds.Count > 0) continue;

            // Not in its founding year: a faith preached this year has a congregation of one that
            // is added immediately after, and would otherwise be pronounced dead on arrival.
            if (faith.FoundedYear == year) continue;

            faith.EndedYear = year;

            var data = new DetMap<string, string>();
            int lasted = year - faith.FoundedYear;
            if (lasted > 0) data["years"] = Chronicle.Years(lasted);

            world.Chronicle.Record(year, EventKind.ReligionFaded, faith.Id, data: data);
        }
    }

    /// <summary>
    /// Brings each realm's official faith into line with its seat of government.
    /// </summary>
    /// <remarks>
    /// A realm changes faith two ways, and both arrive here: its capital converts, or it loses the
    /// capital it had and the next-largest town it falls back on believes something else. The
    /// second is why this is worth an event — a realm can change religion having converted nobody.
    /// </remarks>
    private static void SyncStateFaiths(WorldState world, int year)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            if (civilization.CapitalId.IsNone || !world.Settlements.Contains(civilization.CapitalId))
            {
                continue;
            }

            EntityId seatFaith = world.Settlements[civilization.CapitalId].ReligionId;
            if (seatFaith == civilization.StateReligionId || seatFaith.IsNone) continue;

            civilization.StateReligionId = seatFaith;

            var data = new DetMap<string, string>();
            if (!civilization.CurrentRulerId.IsNone && world.Figures.Contains(civilization.CurrentRulerId))
            {
                data["ruler"] = world.Figures[civilization.CurrentRulerId].FullName;
            }

            world.Chronicle.Record(
                year,
                EventKind.StateFaithChanged,
                civilization.Id,
                obj: seatFaith,
                location: civilization.CapitalId,
                data: data);
        }
    }
}
