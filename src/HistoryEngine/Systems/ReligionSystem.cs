using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Terrain;
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
/// <para><b>Tradition is the brake, and so is zealotry.</b> Without one, faiths chase each other
/// across the map for centuries and the chronicle fills with conversions that mean nothing. A
/// settlement resists in proportion to its culture's tradition, how long it has held what it
/// holds, and how zealous the faith it already follows is, so an ancient holy city is nearly
/// immovable while a young frontier village turns twice in a generation.</para>
///
/// <para><b>A faith is more than fervour.</b> Fervour is still how hard it presses outwards;
/// the rest of its character — gods, church, clergy, tolerance, the likelihood it splits —
/// is rolled once at founding and read by conversion, schism, holy-site form and who may
/// hold a temple. A later congregation that believes something else is a schism, not a
/// revision of the parent.</para>
///
/// <para>Samples no terrain.</para>
/// </remarks>
public sealed class ReligionSystem : ISystem
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

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        IRng rng = world.Root.Fork(Name, year);

        List<DetMap<EntityId, double>> presence = Presence(world);

        Convert(world, presence, year, rng);
        Found(world, year, rng);
        Schism(world, year, rng);
        Fade(world, year);
        SyncStateFaiths(world, year);
        ConvertTheFaithless(world);
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

                bool occupied = !settlement.ReligionId.IsNone;
                double holdfast = 1.0;
                double kinship = 1.0;
                if (occupied && world.Religions.Contains(settlement.ReligionId))
                {
                    Religion held = world.Religions[settlement.ReligionId];
                    holdfast = held.Character.Holdfast();
                    if (faith.KindredTo(held))
                    {
                        kinship = DetMath.Lerp(1.0, 1.32, faith.Character.Syncretism);
                    }
                }

                double chance = ConversionChance
                                * DetMath.Clamp01(pull)
                                * (0.35 + (culture.Values.Piety * 0.9))
                                * faith.Character.OutwardPressure(occupied)
                                * Resistance(culture, settlement, year)
                                * holdfast
                                * kinship;

                if (!rng.Chance(DetMath.Clamp01(chance))) continue;

                Adopt(world, settlement, faith, culture, year, founding: false);
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

    private static void Adopt(
        WorldState world,
        Settlement settlement,
        Religion faith,
        Culture culture,
        int year,
        bool founding)
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

        EstablishHolySite(world, settlement, faith, culture, year, required: founding);
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

                Adopt(world, settlement, faith, culture, year, founding: true);
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

                double chance = SchismChance * faith.Character.SchismWeight();
                if (rng.Chance(chance)) splitting.Add(settlement);
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

            Adopt(world, settlement, splinter, culture, year, founding: true);
        }
    }

    // -----------------------------------------------------------------------
    // Sacred places
    // -----------------------------------------------------------------------

    /// <summary>
    /// Gives every new faith a birthplace and lets established congregations raise additional
    /// houses of worship without making one inevitable in every hamlet.
    /// </summary>
    private static void EstablishHolySite(
        WorldState world,
        Settlement settlement,
        Religion faith,
        Culture culture,
        int year,
        bool required)
    {
        ulong pair = Hash.Combine(
            (ulong)settlement.Id.ToDiscriminator(),
            (ulong)faith.Id.ToDiscriminator());
        IRng own = world.Root.Fork("religion.holy-site", unchecked((long)pair));

        // Raising a house of worship is a thing somebody pays for, so it takes the realm's
        // effective piety rather than the people's — a devout ruler builds where his predecessor
        // did not, and a realm lately visited by plague builds more than it used to. Conversion
        // itself stays cultural: a congregation is a settlement, not a court.
        CultureValues values = world.Civilizations.Contains(settlement.CivilizationId)
            ? world.ValuesFor(world.Civilizations[settlement.CivilizationId])
            : culture.Values;

        double chance = settlement.Tier switch
        {
            SettlementTier.City => 0.72,
            SettlementTier.Town => 0.52,
            SettlementTier.Village => 0.32,
            _ => 0.08,
        };
        chance *= 0.55 + values.Piety;

        if (!required && !own.Chance(DetMath.Clamp01(chance))) return;

        HolySiteKind kind = ChooseHolySiteKind(settlement, faith, own);
        bool independent = settlement.Specialization == SettlementSpecialization.Shrine
                           || own.Chance(0.20 + (values.Piety * 0.22) + faith.Character.IndependentSiteBias());
        Point2 position = new(settlement.X, settlement.Z);
        if (independent)
        {
            position = ChooseIndependentSite(world, settlement);
            independent = position != new Point2(settlement.X, settlement.Z);
        }

        if (AlreadyConsecrated(world, faith.Id, position)) return;

        EntityId id = world.HolySites.NextId;
        string name = $"{HolySiteKindLabel(kind)} of {world.Names.ForHolySite(id, culture)}";
        HolySiteDescription description = HolySites.Compose(
            world, id, kind, faith, culture, settlement, independent, year);

        var site = new HolySite(
            id,
            name,
            kind,
            faith.Id,
            settlement.RegionId,
            independent ? EntityId.None : settlement.Id,
            position.X,
            position.Z,
            year,
            description);

        world.HolySites.Add(site);
        world.Chronicle.Record(
            year,
            EventKind.HolySiteFounded,
            site.Id,
            obj: faith.Id,
            location: site.IsWithinSettlement ? settlement.Id : settlement.RegionId);
    }

    /// <summary>
    /// Whether this faith already keeps a sacred place on this exact ground.
    /// </summary>
    /// <remarks>
    /// A congregation can lose a settlement to another faith and win it back generations later,
    /// and both the enclosed and the independent location are pure functions of the settlement —
    /// the same town, the same hill. Without this check the returning faith raises a second house
    /// of worship on top of the first: same faith, same coordinate, and usually the same kind,
    /// because the founding draws are keyed to the settlement and the faith rather than to the
    /// year. What actually happens is that the returning congregation reuses what it built.
    /// </remarks>
    private static bool AlreadyConsecrated(WorldState world, EntityId religionId, Point2 position)
    {
        foreach (HolySite site in world.HolySites)
        {
            if (site.ReligionId == religionId && site.X == position.X && site.Z == position.Z)
            {
                return true;
            }
        }

        return false;
    }

    private static HolySiteKind ChooseHolySiteKind(Settlement settlement, Religion faith, IRng rng)
    {
        // A settlement already known for pilgrimage builds the thing its character promises.
        if (settlement.Specialization == SettlementSpecialization.Shrine)
        {
            return HolySiteKind.Shrine;
        }

        (HolySiteKind Kind, int Weight)[] weights = faith.Character.HolySiteWeights();
        int total = 0;
        foreach ((HolySiteKind _, int weight) in weights)
        {
            if (weight > 0) total += weight;
        }

        if (total <= 0) return HolySiteKind.Shrine;

        int roll = rng.NextInt(total);
        int acc = 0;
        foreach ((HolySiteKind kind, int weight) in weights)
        {
            if (weight <= 0) continue;
            acc += weight;
            if (roll < acc) return kind;
        }

        return HolySiteKind.Sanctuary;
    }

    /// <summary>
    /// Picks an already-refined land point away from the settlement. Every settlement's region
    /// was evaluated on this same four-per-axis grid when the settlement was founded, so this
    /// normally costs no new samples while still giving the permanent location exact terrain.
    /// </summary>
    private static Point2 ChooseIndependentSite(WorldState world, Settlement settlement)
    {
        Region region = world.Regions[settlement.RegionId];
        int stride = Math.Max(8, region.Bounds.Width / 4);
        double minimumDistanceSquared = stride * stride * 0.75;

        Point2 best = new(settlement.X, settlement.Z);
        double bestScore = double.NegativeInfinity;

        foreach (KeyValuePair<Point2, TerrainSample> candidate
                 in world.Terrain.RefinedPoints(region.Bounds, stride))
        {
            TerrainSample sample = candidate.Value;
            if (sample.IsSubmerged || sample.Water != WaterKind.None) continue;

            double distanceSquared = world.DistanceSquared(
                settlement.X, settlement.Z, candidate.Key.X, candidate.Key.Z);
            if (distanceSquared < minimumDistanceSquared) continue;

            // Sacred places favour visible high ground, but remain close enough to the people
            // who raised them to be reached as pilgrimage rather than expedition.
            double distance = DetMath.Sqrt(distanceSquared);
            double height = DetMath.InverseLerp(0.0, 1800.0, sample.Height);
            double proximity = DetMath.InverseLerp(region.Bounds.Width * 1.2, stride, distance);
            double score = (height * 0.6) + (proximity * 0.4);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate.Key;
            }
        }

        return best;
    }

    private static string HolySiteKindLabel(HolySiteKind kind) => kind switch
    {
        HolySiteKind.Shrine => "Shrine",
        HolySiteKind.Temple => "Temple",
        HolySiteKind.Church => "Church",
        HolySiteKind.Monastery => "Monastery",
        _ => "Sanctuary",
    };

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
        double fervour = DetMath.Clamp01(own.NextDouble(0.15, 0.85) + (culture.Values.Piety * 0.2));
        IRng character = own.Fork("character");

        FaithCharacter identity = parentId.IsNone || !world.Religions.Contains(parentId)
            ? FaithCharacter.Roll(character, culture.Values, fervour)
            : FaithCharacter.FromParent(
                world.Religions[parentId].Character, character, culture.Values, fervour);

        var faith = new Religion(
            id,
            world.Names.ForReligion(id, culture),
            culture.Id,
            settlement.Id,
            year,
            identity)
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
        // The realm's own high priest, where it has one. This mostly reaches schisms rather than
        // foundings — a realm with no faith at all has no high priest to break away from one —
        // and a schism led by the establishment's own senior cleric is what a reform actually is.
        // The fallback below is the original placeholder, and reads like one.
        Figure? cleric = Offices.HolderOf(world, civilization, OfficeKind.HighPriest);
        if (cleric is not null && cleric.Id != civilization.CurrentRulerId) return cleric.Id;

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
            EntityId[]? extra = null;
            if (world.NamePerson(data, "ruler", civilization.CurrentRulerId))
            {
                extra = new[] { civilization.CurrentRulerId };
            }

            world.Chronicle.Record(
                year,
                EventKind.StateFaithChanged,
                civilization.Id,
                obj: seatFaith,
                location: civilization.CapitalId,
                extra: extra,
                data: data.Count == 0 ? null : data);
        }
    }

    /// <summary>
    /// Gives a faith to living people who have never had one, once one reaches them.
    /// </summary>
    /// <remarks>
    /// <para>Founding rulers, and everyone born before the first sermon, were raised with nothing
    /// to follow. Their disposition was already rolled, and converting a town does not rewrite a
    /// person — but leaving them faithless forever would make personal religion a property of
    /// the late-born only. Residence first, then the realm: the same order a newborn uses,
    /// without walking parents, because a grown figure's parents are not who they live among.</para>
    ///
    /// <para><b>A celibate faith does not enrol a married priest.</b> This is the one way a vow
    /// could reach someone no guard could have stopped: a figure who married and took orders in a
    /// realm that had no faith at all breaks no rule doing it, and a celibate faith arriving in
    /// the town twenty years later would make them a married priest of it retroactively. The
    /// alternatives are worse — releasing them from orders rewrites a career the chronicle already
    /// recorded, and ending the marriage lets a faith reach into a household it did not govern
    /// when the household was made. They keep both and stay faithless, which is what they were.
    /// If the ambient faith later changes to one that permits it, they convert like anyone
    /// else.</para>
    /// </remarks>
    private static void ConvertTheFaithless(WorldState world)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || !figure.ReligionId.IsNone) continue;

            EntityId home = world.ResidenceOf(figure);
            if (world.Settlements.Contains(home))
            {
                EntityId held = world.Settlements[home].ReligionId;
                if (!held.IsNone)
                {
                    if (WouldBreakAVow(world, figure, held)) continue;

                    figure.ReligionId = held;
                    continue;
                }
            }

            if (!world.Civilizations.Contains(figure.CivilizationId)) continue;

            EntityId state = world.Civilizations[figure.CivilizationId].StateReligionId;
            if (state.IsNone || WouldBreakAVow(world, figure, state)) continue;

            figure.ReligionId = state;
        }
    }

    /// <summary>Whether professing this faith would put a married cleric under its vow.</summary>
    private static bool WouldBreakAVow(WorldState world, Figure figure, EntityId faithId) =>
        figure.Occupation == Occupation.Clergy
        && figure.IsMarried
        && world.Religions.Contains(faithId)
        && world.Religions[faithId].Character.CelibateClergy;
}
