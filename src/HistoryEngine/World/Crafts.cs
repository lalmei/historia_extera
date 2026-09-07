using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// Which trade a guildsman practises, and where it could be practised at all.
/// </summary>
/// <remarks>
/// <para><b>The place decides what is on offer; the household decides which of them is taken.</b>
/// Every craft here needs something real — ore and fuel, sheltered water, a wool hinterland, falling
/// water, a road — so the set a person can choose from is a fact about where they live, and a craft
/// whose input is absent is not offered rather than merely unlikely. That is the same discipline
/// <see cref="Systems.SpecializationSystem"/> applies when fishing scores impossible inland.</para>
///
/// <para><b>A guild pulls harder than a career does.</b> <see cref="Occupations.FamilyPull"/> is 0.70
/// on the choice of a career, which leaves a marshal's child likelier than average to serve without
/// making it certain. Apprenticeship inside a guild was not like that: the son of a smith in a
/// mining town was, in the ordinary case, a smith. <see cref="HouseholdPull"/> is therefore much
/// larger, and large enough that craft dynasties are legible across generations — which is the
/// point, because a craft nobody inherits is a label rather than a lineage.</para>
///
/// <para><b>Once, and for life.</b> A craft is taken at majority and kept, including through offices
/// and after death: a man who was a mason and then sat as guild master is still a mason, and the
/// record should be able to say so. Nothing here reassigns.</para>
///
/// <para>Forked on the figure's own id, so the year somebody comes of age cannot change what they
/// become and a later sibling cannot reshuffle an earlier one.</para>
/// </remarks>
public static class Crafts
{
    /// <summary>
    /// How hard a parent's craft pulls, at no independence.
    /// </summary>
    /// <remarks>
    /// Thirteen times <see cref="Occupations.FamilyPull"/>, and the size is measured rather than
    /// chosen: against base weights that top out below one, a pull of 2.1 had children of craftsmen
    /// taking the family trade 41% of the time across the five-seed panel and 6.0 took it to 55% —
    /// barely more than the local weights alone would give, and not a dynasty. A guild admitted its own members' sons on
    /// terms nobody else got, and the exceptions that remain are the right ones: the independent,
    /// and the ones whose home town cannot support the trade at all.
    /// </remarks>
    private const double HouseholdPull = 9.0;

    /// <summary>Geologic activity above which a region has building stone worth a mason.</summary>
    /// <remarks>
    /// Deliberately not <see cref="Specializations.OreThreshold"/>. Good building stone is not ore
    /// country — it is broken ground and hard rock, which is why this reads
    /// <see cref="Region.Ruggedness"/> first and geology second.
    ///
    /// <para>Low, and measured: at 0.30 with a height fallback of 800m, stone was reachable from 22
    /// settlements in 196 across the panel, and masons came out 0.4% of all guildsmen — a world
    /// whose walls and temples are built by almost nobody. Most inhabited country has usable
    /// building stone within reach of a cart; what it does not have is a <em>quarry</em>, which is
    /// a different question and belongs to the settlement's trade.</para>
    /// </remarks>
    private const double StoneRuggedness = 0.18;

    /// <summary>Rainfall at which a region grows timber worth working, given the warmth for it.</summary>
    private const double TimberRainfall = 0.38;

    /// <summary>Fertility at which a region grows grain in quantity worth milling.</summary>
    private const double GrainFertility = 0.35;

    /// <summary>How far a craft will look for its material along a road or a river.</summary>
    /// <remarks>
    /// A route, not a radius. Metal, wool and hides travelled; the question a craft asks is not
    /// "what is under this town" but "what reaches it", and the trade-route graph is the engine's
    /// existing answer to that.
    /// </remarks>
    private const int SupplyHops = 1;

    /// <summary>Every craft, in a fixed order so weighted draws are reproducible.</summary>
    private static readonly Craft[] All =
    {
        Craft.Smith,
        Craft.Armourer,
        Craft.Goldsmith,
        Craft.Mason,
        Craft.Carpenter,
        Craft.Shipwright,
        Craft.Sailor,
        Craft.Weaver,
        Craft.Dyer,
        Craft.Tanner,
        Craft.Potter,
        Craft.CharcoalBurner,
        Craft.Glassblower,
        Craft.Cooper,
        Craft.Wheelwright,
        Craft.Miller,
        Craft.Baker,
        Craft.Brewer,
        Craft.Salter,
        Craft.Bookbinder,
        Craft.Apothecary,
    };

    /// <summary>How the chronicle names the trade.</summary>
    public static string Phrase(Craft craft) => craft switch
    {
        Craft.Smith => "the forge",
        Craft.Armourer => "the armourer's trade",
        Craft.Goldsmith => "the goldsmith's trade",
        Craft.Mason => "the mason's trade",
        Craft.Carpenter => "carpentry",
        Craft.Shipwright => "the building of ships",
        Craft.Sailor => "the sea",
        Craft.Weaver => "the loom",
        Craft.Dyer => "the dyer's trade",
        Craft.Tanner => "the tannery",
        Craft.Potter => "the potter's wheel",
        Craft.CharcoalBurner => "the charcoal pits",
        Craft.Glassblower => "the glasshouse",
        Craft.Cooper => "the cooper's trade",
        Craft.Wheelwright => "the wheelwright's trade",
        Craft.Miller => "the mill",
        Craft.Baker => "the bakehouse",
        Craft.Brewer => "the brewhouse",
        Craft.Salter => "the salt pans",
        Craft.Bookbinder => "the binding of books",
        Craft.Apothecary => "the apothecary's trade",
        _ => "a craft",
    };

    /// <summary>The single word a list or a title uses.</summary>
    public static string Label(Craft craft) => craft switch
    {
        Craft.Smith => "smith",
        Craft.Armourer => "armourer",
        Craft.Goldsmith => "goldsmith",
        Craft.Mason => "mason",
        Craft.Carpenter => "carpenter",
        Craft.Shipwright => "shipwright",
        Craft.Sailor => "sailor",
        Craft.Weaver => "weaver",
        Craft.Dyer => "dyer",
        Craft.Tanner => "tanner",
        Craft.Potter => "potter",
        Craft.CharcoalBurner => "charcoal burner",
        Craft.Glassblower => "glassblower",
        Craft.Cooper => "cooper",
        Craft.Wheelwright => "wheelwright",
        Craft.Miller => "miller",
        Craft.Baker => "baker",
        Craft.Brewer => "brewer",
        Craft.Salter => "salter",
        Craft.Bookbinder => "bookbinder",
        Craft.Apothecary => "apothecary",
        _ => "guildsman",
    };

    /// <summary>
    /// Gives this figure a craft if they are a guildsman, are old enough, and have none.
    /// </summary>
    /// <remarks>
    /// Called wherever somebody takes up <see cref="Occupation.Guild"/>. Silent for everyone else,
    /// and silent for a guildsman who already has a craft — including one who left the guild for an
    /// office and came back, because a trade is not unlearned.
    /// </remarks>
    public static void Ensure(WorldState world, Figure figure, int year)
    {
        if (figure.Occupation != Occupation.Guild) return;
        if (figure.Craft != Craft.None) return;

        EntityId home = world.ResidenceOf(figure);
        if (!world.Settlements.Contains(home)) return;

        Supply supply = SupplyAt(world, world.Settlements[home]);
        double[] weights = Weights(world, figure, supply);

        double total = 0.0;
        foreach (double weight in weights) total += weight;

        // A town that can support no craft at all leaves them a guildsman of no stated trade,
        // which is a better record than inventing an input the place does not have.
        if (total <= 0.0) return;

        IRng rng = world.Root.Fork("craft", figure.Id.ToDiscriminator());
        Craft chosen = rng.PickWeighted(All, craft => weights[IndexOf(craft)]);

        Take(world, figure, chosen, year);
    }

    /// <summary>
    /// Whether this settlement can support this craft at all.
    /// </summary>
    /// <remarks>
    /// The one place that says what each craft needs. Read by assignment, and meant to be read by
    /// anything that later asks whether a town could hold a trade it does not yet hold.
    /// </remarks>
    public static bool Supports(WorldState world, Settlement settlement, Craft craft) =>
        Weight(craft, SupplyAt(world, settlement)) > 0.0;

    /// <summary>The complete, inspectable pull on a first craft before its one random draw.</summary>
    internal static double[] Weights(WorldState world, Figure figure, Supply supply)
    {
        var weights = new double[All.Length];
        for (int i = 0; i < All.Length; i++) weights[i] = Weight(All[i], supply);

        double independence = figure.Disposition.Independence;
        PullToward(world, figure.MotherId, independence, weights);
        PullToward(world, figure.FatherId, independence, weights);

        return weights;
    }

    /// <summary>
    /// What a settlement can put in a craftsman's hands.
    /// </summary>
    /// <remarks>
    /// Read off the region grid, the settlement's own trade and the routes that reach it — no
    /// terrain sampling, because every field here already exists on <see cref="Region"/> or on the
    /// settlement itself.
    /// </remarks>
    internal readonly record struct Supply(
        SettlementTier Tier,
        bool IsCapital,
        bool Metal,
        bool Stone,
        bool Timber,
        bool Wool,
        bool Hides,
        bool Grain,
        bool Clay,
        bool WaterPower,
        bool Shelter,
        bool Seagoing,
        bool SaltGround,
        bool Wealth,
        bool Connected);

    internal static Supply SupplyAt(WorldState world, Settlement settlement)
    {
        Region region = world.Regions[settlement.RegionId];
        SettlementSpecialization trade = settlement.Specialization;

        bool ore = region.GeologicActivity >= Specializations.OreThreshold
                   || trade == SettlementSpecialization.Mining
                   || Reaches(world, settlement, SettlementSpecialization.Mining);

        bool timber = region.Rainfall >= TimberRainfall
                      && region.Temperature > 0.20
                      && region.Biome is Biome.TemperateForest
                          or Biome.Taiga
                          or Biome.TropicalForest
                          or Biome.Grassland
                          or Biome.Wetland;

        bool pastoral = trade == SettlementSpecialization.Pastoral
                        || Reaches(world, settlement, SettlementSpecialization.Pastoral);

        bool farming = trade == SettlementSpecialization.Farming
                       || region.Fertility >= GrainFertility
                       || Reaches(world, settlement, SettlementSpecialization.Farming);

        int degree = TradeRoutes.Degree(world, settlement.Id);

        return new Supply(
            Tier: settlement.Tier,
            IsCapital: settlement.IsCapital,

            // Ore is worth nothing without fuel to work it, which is why the metal chain asks for
            // both and why an ore region with no wood is not an ironworking region.
            Metal: ore && timber,
            Stone: region.Ruggedness >= StoneRuggedness
                   || region.GeologicActivity >= 0.35
                   || region.MeanHeight > 500.0,
            Timber: timber,

            // Not herding country alone. Sheep were kept on mixed farms everywhere, and flax grows
            // on wet ground — asking for a Pastoral settlement specialization meant wool existed in
            // 13 settlements out of 196 across the panel, which would make weaving a rarity in a
            // world where cloth was the largest thing anybody made.
            Wool: pastoral
                  || (farming && region.Rainfall > 0.30)
                  || region.Biome is Biome.Grassland or Biome.Steppe or Biome.Savanna
                  || (region.RiverAccess > 0.30 && region.Rainfall > 0.45),

            // Hides come off the same animals, so herding country supplies them outright and
            // farming country supplies them in the smaller quantity a village tannery works from.
            Hides: pastoral || farming,
            Grain: farming,

            // Clay follows still and slow water rather than the fall a mill wants.
            Clay: region.RiverAccess > 0.25 || region.Biome == Biome.Wetland,
            WaterPower: region.RiverAccess > 0.35 || region.HasRiver,
            Shelter: region.HarbourQuality > 0.35,
            Seagoing: (region.IsCoastal || region.HarbourQuality > 0.0) && degree > 0,

            // Pans need a coast the sun works on; springs need the geology that carries brine.
            // Most places have neither, which is the whole history of salt.
            SaltGround: (region.IsCoastal && region.Temperature > 0.60 && region.Rainfall < 0.40)
                        || (region.GeologicActivity > 0.55 && region.Rainfall < 0.35),
            Wealth: settlement.IsCapital
                    || settlement.Tier == SettlementTier.City
                    || (settlement.Tier >= SettlementTier.Town && degree >= 2),
            Connected: degree > 0);
    }

    /// <summary>
    /// What a craft is worth in a place that can support it, and zero in one that cannot.
    /// </summary>
    /// <remarks>
    /// <para><b>These now have measurements behind them.</b> They were set as a shape — the trades
    /// a town has many of are worth more than the trades it has one of — with a note that they
    /// would want calibrating once assignment was running. This is that calibration, on the
    /// five-seed panel at a thousand years: 3,634 guildsmen with a craft, in a world where
    /// <see cref="SettlementSpecialization.Crafts"/> is reachable, which it was not when the shape
    /// was drawn.</para>
    ///
    /// <para><b>A craft's share is its weight times its exposure, and the two must be read
    /// together.</b> The gate decides how much of the world can practise a trade at all, and only
    /// then does the weight decide how much of that world does. Measured across 472 living
    /// settlements holding 848,566 people:</para>
    ///
    /// <list type="table">
    /// <item><description>weaver 15.6% of guildsmen, on 99.8% of people — the broadest gate in the
    /// table meeting the second-heaviest weight. Textiles being a world's largest single trade is
    /// both what the guild rolls say and what <see cref="Supply.Wool"/> was widened to allow;</description></item>
    /// <item><description>carpenter 9.9%, baker 9.1%, miller 7.8%, smith 7.5%, mason 7.2%, charcoal
    /// burner 6.6%, potter 6.1%, tanner 6.0% — the ordinary trades of a town, all on gates that
    /// reach two thirds of the world or better;</description></item>
    /// <item><description>goldsmith 2.3% and glassblower 0.7%, both on gates that reach four fifths
    /// of the world's people. They are rare because their weights are small, which is the intended
    /// reason: wealth admits a goldsmith, it does not make one common;</description></item>
    /// <item><description>salter 0.6% on 6.1% of people, and sailor 4.0% on 23.5% — rare because
    /// the ground is rare, not because the number is small. Salt in particular is meant to
    /// be a fact about geography rather than about guilds.</description></item>
    /// </list>
    ///
    /// <para><b>The one number this pass moved is the shipwright's</b>, from 0.20 to 0.55. Sheltered
    /// water and timber is the narrowest gate here — 13 settlements of 472 — and at 0.20 on top of
    /// that the trade came out five men in five thousand years of history, which is a costed craft
    /// that does not exist. A weight is what a trade is worth <em>in a place that has it</em>, and
    /// in a town built around a harbour shipbuilding is a principal trade rather than a curiosity;
    /// the old number said the opposite. It takes the trade from 5 men to 15, or 0.1% to 0.4%, and
    /// moves nothing else by more than a tenth of a point. The world-level share stays small,
    /// because it should: it is the gate that makes shipwrights rare, and the gate is right.</para>
    ///
    /// <para>Tier floors are part of the gate rather than a modifier. A village bakes at home and
    /// makes its own cloth; a baker or a dyer as a <em>recorded trade</em> needs a market to sell
    /// into, and admitting them below a town would fill villages with occupations that in life were
    /// somebody's second job.</para>
    /// </remarks>
    private static double Weight(Craft craft, Supply s) => craft switch
    {
        Craft.Smith => s.Metal ? 1.00 : 0.0,
        Craft.Armourer => s.Metal && s.Tier >= SettlementTier.Town ? 0.16 : 0.0,
        Craft.Goldsmith => s.Wealth && s.Tier >= SettlementTier.Town ? 0.14 : 0.0,
        Craft.Mason => s.Stone ? 0.62 : 0.0,
        Craft.Carpenter => s.Timber ? 0.95 : 0.0,
        Craft.Shipwright => s.Shelter && s.Timber && s.Tier >= SettlementTier.Town ? 0.55 : 0.0,
        Craft.Sailor => s.Seagoing ? 0.70 : 0.0,
        Craft.Weaver => s.Wool ? 0.90 : 0.0,
        Craft.Dyer => s.Wool && s.WaterPower && s.Tier >= SettlementTier.Town ? 0.24 : 0.0,
        Craft.Tanner => s.Hides && s.WaterPower ? 0.55 : 0.0,
        Craft.Potter => s.Clay && s.Timber ? 0.75 : 0.0,
        Craft.CharcoalBurner => s.Timber ? 0.58 : 0.0,
        Craft.Glassblower => s.Timber && s.Wealth && s.Tier >= SettlementTier.Town ? 0.10 : 0.0,
        Craft.Cooper => s.Timber && (s.Connected || s.Tier >= SettlementTier.Town) ? 0.50 : 0.0,
        Craft.Wheelwright => s.Timber && s.Metal && s.Tier >= SettlementTier.Town ? 0.34 : 0.0,
        Craft.Miller => s.Grain && s.WaterPower ? 0.80 : 0.0,
        Craft.Baker => s.Grain && s.Tier >= SettlementTier.Town ? 0.66 : 0.0,
        Craft.Brewer => s.Grain && s.WaterPower && s.Timber && s.Tier >= SettlementTier.Town ? 0.40 : 0.0,
        Craft.Salter => s.SaltGround ? 0.30 : 0.0,
        Craft.Bookbinder => s.Hides && s.Tier >= SettlementTier.Town && s.Wealth ? 0.12 : 0.0,
        Craft.Apothecary => s.Connected && s.Tier >= SettlementTier.Town ? 0.18 : 0.0,
        _ => 0.0,
    };

    /// <summary>Whether a settlement of the given trade is one route away.</summary>
    private static bool Reaches(WorldState world, Settlement settlement, SettlementSpecialization trade)
    {
        // One hop, deliberately. Two would make every connected town supply every material in the
        // world, which is the opposite of what a gate is for.
        _ = SupplyHops;

        foreach (TradeRoute route in TradeRoutes.From(world, settlement.Id))
        {
            EntityId otherId = route.SettlementAId == settlement.Id
                ? route.SettlementBId
                : route.SettlementAId;
            if (!world.Settlements.Contains(otherId)) continue;
            if (world.Settlements[otherId].Specialization == trade) return true;
        }

        return false;
    }

    private static void PullToward(
        WorldState world, EntityId parentId, double independence, double[] weights)
    {
        if (!world.Figures.Contains(parentId)) return;

        Craft parent = world.Figures[parentId].Craft;
        if (parent == Craft.None) return;

        // Only toward something the town can actually support. A smith's son in a place with no
        // metal is not a smith, however strong the pull — the gate is the fact, and the pull is a
        // preference among facts.
        int index = IndexOf(parent);
        if (weights[index] <= 0.0) return;

        weights[index] += (1.0 - independence) * HouseholdPull;
    }

    private static void Take(WorldState world, Figure figure, Craft craft, int year)
    {
        figure.Craft = craft;
        if (craft == Craft.None || !figure.IsAlive) return;

        world.Chronicle.Record(
            year,
            EventKind.CraftTaken,
            figure.Id,
            location: world.ResidenceOf(figure),
            data: Chronicle.Data(("craft", Label(craft))),
            significance: Significance.Routine);
    }

    private static int IndexOf(Craft craft) => (int)craft - 1;
}
