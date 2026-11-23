namespace HistoryEngine.Entities;

/// <summary>
/// What a settlement is chiefly known for. Explicit values — part of the export format.
/// </summary>
public enum SettlementSpecialization
{
    /// <summary>Not yet established. Hamlets have no character of their own.</summary>
    None = 0,
    Farming = 1,
    Pastoral = 2,
    Fishing = 3,
    Mining = 4,
    Trade = 5,
    Crafts = 6,
    Shrine = 7,
    Military = 8,

    // Quarry was declared here with a full set of curves below and no way to be chosen: it was
    // absent from SpecializationSystem.Candidates, and when M30 gated it on the ground it would
    // need — broken country, low geology so it does not simply take mining's — the panel found
    // sixteen settlements in 787 standing on it, every one of them on soil of fertility 0.63 to
    // 0.87 where farming already scores 0.77 to 0.95. There is no quarry town to be had until
    // siting can put a settlement on hard ground; it never puts one above 597m or past a regional
    // ruggedness of 0.32 at the ninetieth percentile. Costed and unreachable is the worst of the
    // three states, so the cost came out. SiteCharacter.Quarry stays: that is the ground, which is
    // a different question and carries no numbers.
}

/// <summary>
/// How a specialization changes what a settlement can support.
/// </summary>
/// <remarks>
/// <para>These are the numbers that make specialization matter rather than decorate. A fishing
/// town and a farming town on the same ground reach different sizes and fail in different years,
/// which is the whole point: it gives geography a second-order effect on history, not just a
/// first-order one.</para>
///
/// <para><see cref="HarvestSensitivity"/> is the interesting field. A farming town lives and dies
/// by the harvest; a mining town buys its food and mostly does not care, and a shrine town is
/// sustained by pilgrims regardless. So a bad regional decade empties the farms and leaves the
/// mines standing — the sort of asymmetry that makes a chronicle worth reading.</para>
/// </remarks>
public static class Specializations
{
    /// <summary>
    /// Geologic activity at which there is ore worth working, in [0, 1].
    /// </summary>
    /// <remarks>
    /// <para>The one line in the engine between rock and a mine, and it is shared on purpose. Three
    /// decisions ask the question — whether a realm should send a party for a deposit, whether the
    /// ground it stood on can be recorded as a mine site, and whether a village may be known for
    /// mining — and if any of them drew the line somewhere else the chronicle would contain camps
    /// founded to work ore that can never be known for it.</para>
    ///
    /// <para>There is no deposit map under this, and there should not be one until something needs
    /// what a map would add. <see cref="World.Region.GeologicActivity"/> above this threshold is
    /// what "there is ore here" means.</para>
    /// </remarks>
    public const double OreThreshold = 0.35;

    /// <summary>Multiplier on the fertility-derived component of carrying capacity.</summary>
    public static double FertilityWeight(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 1.35,
        SettlementSpecialization.Pastoral => 0.70,
        SettlementSpecialization.Fishing => 0.55,
        SettlementSpecialization.Mining => 0.35,
        SettlementSpecialization.Trade => 0.65,
        SettlementSpecialization.Crafts => 0.80,
        SettlementSpecialization.Shrine => 0.60,
        SettlementSpecialization.Military => 0.45,
        _ => 1.0,
    };

    /// <summary>
    /// Capacity the site itself yields regardless of the surrounding land, in people.
    /// </summary>
    /// <remarks>
    /// <para>The ore body, the fishery, the spring the pilgrims come for: what a settlement gets
    /// from the ground it stands on rather than from the fields around it. Small on purpose, and it
    /// did not used to be.</para>
    ///
    /// <para><b>Why it shrank.</b> These were once 1,400–2,600 for the five trades that do not feed
    /// themselves, granted unconditionally the year a village outgrew a hamlet. That is not a floor
    /// a settlement stands on, it is a town handed out for free, and it swamped everything else in
    /// <see cref="Systems.PopulationSystem.CapacityOf"/>. The signature is visible per trade: on a
    /// thousand-year run of seed 42, the 47 mining towns had a median population of 2,192 and a
    /// maximum of 3,185, on ground ranging from barren to excellent. They were not responding to
    /// the world at all. They were reporting this constant, because nothing else in the calculation
    /// was large enough to be heard over it.</para>
    ///
    /// <para><b>What replaced it.</b> The claim the old docstring made — that a mining town or a
    /// trading port "stands on its trade rather than on its soil" — is now literally true, because
    /// <see cref="ImportReliance"/> sources that capacity from the settlement's actual trade
    /// routes. A trading port with no route to anywhere is a village, which is correct, and a
    /// closed route now costs a town people over the following decade.</para>
    /// </remarks>
    public static double SiteCapacity(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 60.0,
        SettlementSpecialization.Pastoral => 180.0,
        SettlementSpecialization.Fishing => 620.0,
        SettlementSpecialization.Mining => 400.0,
        SettlementSpecialization.Trade => 240.0,
        SettlementSpecialization.Crafts => 260.0,
        SettlementSpecialization.Shrine => 300.0,
        SettlementSpecialization.Military => 200.0,
        _ => 110.0,
    };

    /// <summary>
    /// People one unit of live trade-route traffic can feed here, beyond what the land bears.
    /// </summary>
    /// <remarks>
    /// <para>What a settlement eats that it did not grow. This is the term that makes a city, and
    /// before it existed nothing did: carrying capacity read the land, the harvest, the distance to
    /// the capital and the culture, and not one thing about whether the place was connected to
    /// anywhere. A settlement on four busy routes had the same ceiling as one at the end of a
    /// track, so <see cref="World.TradeRoute"/> — a system with 421 routes and measured yearly
    /// traffic on each — reached population by no path at all, and fed only plague and the
    /// circulation of books.</para>
    ///
    /// <para>Ordered by how much of its living a trade takes from elsewhere. A farming village
    /// barely notices the road; a market town is the road. That ordering is deliberately the mirror
    /// of <see cref="FertilityWeight"/>, so the two terms hand the hierarchy between them: the land
    /// decides how large a place can grow on its own, and commerce decides which of them become
    /// more than that. Cities concentrating a hinterland's surplus rather than standing on
    /// unusually good fields of their own is both what central-place geography says and what gives
    /// the size distribution a tail instead of a hump.</para>
    /// </remarks>
    public static double ImportReliance(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 120.0,
        SettlementSpecialization.Pastoral => 150.0,
        SettlementSpecialization.Fishing => 420.0,
        SettlementSpecialization.Mining => 560.0,
        SettlementSpecialization.Trade => 900.0,
        SettlementSpecialization.Crafts => 700.0,
        SettlementSpecialization.Shrine => 480.0,
        SettlementSpecialization.Military => 1000.0,
        _ => 130.0,
    };

    /// <summary>
    /// How much a poor harvest hurts, from 0 (immune) to 1 (fully exposed).
    /// </summary>
    public static double HarvestSensitivity(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 1.0,
        SettlementSpecialization.Pastoral => 0.80,
        SettlementSpecialization.Fishing => 0.35,
        SettlementSpecialization.Mining => 0.30,
        SettlementSpecialization.Trade => 0.45,
        SettlementSpecialization.Crafts => 0.55,
        SettlementSpecialization.Shrine => 0.40,
        SettlementSpecialization.Military => 0.85,
        _ => 0.90,
    };

    /// <summary>
    /// How badly distance from the seat of government hurts, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// A mining or trading settlement depends on the realm reaching it; a farming village feeds
    /// itself and barely notices. This is what makes overextended civilizations shed their
    /// furthest holdings first.
    /// </remarks>
    public static double SupplyDependence(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => 0.15,
        SettlementSpecialization.Pastoral => 0.20,
        SettlementSpecialization.Fishing => 0.30,
        SettlementSpecialization.Mining => 0.85,
        SettlementSpecialization.Trade => 0.75,
        SettlementSpecialization.Crafts => 0.60,
        SettlementSpecialization.Shrine => 0.45,
        SettlementSpecialization.Military => 0.85,
        _ => 0.25,
    };

    /// <summary>
    /// The crafts a trade cannot run without (#253).
    /// </summary>
    /// <remarks>
    /// <para><b>A trade was free.</b> A settlement became a mining town because the ground scored
    /// well, and the five curves above then paid out whether or not anyone there could sink a shaft
    /// or work what came out of it. These are the people the trade stands on, stated beside the
    /// curves that say what it yields.</para>
    ///
    /// <para><b>Held is read off the supply, not the roll of recorded figures.</b> A craft is held
    /// where <see cref="World.Crafts.Supports"/> says the place could keep it — iron and fuel by
    /// ground or road, wood for the burner, wool for the loom. Recorded guildsmen are far too thin
    /// a sample to answer the question: most towns have no named smith and plainly had smiths.
    /// So a trade loses its craftsmen the way a place actually lost them, when the road that fed
    /// the forge closed or the woods were out of reach, and regains them when the road reopens.</para>
    ///
    /// <para><b>Only crafts with no tier floor.</b> Every craft here is one a village may hold, so a
    /// shortfall is never just a village being a village. A shipwright and a wheelwright would
    /// belong on the port's list, and both want a town; asking them of a trade that is chosen at
    /// village tier would drag every port until it had already grown without them.</para>
    ///
    /// <para><b>Empty where adding one would make the wrong thing rarer.</b> Farming feeds itself,
    /// and what it needs from a smith is mending rather than making. Herding and fishing are still
    /// the rarest trades after the #275 rebalance — 23 and 48 of 859 settlements across the panel
    /// at a thousand years — and a craft requirement on them would push them back toward the
    /// absence that issue was about. A shrine stands on its pilgrims.</para>
    /// </remarks>
    public static Craft[] RequiredCrafts(SettlementSpecialization specialization) => specialization switch
    {
        // Sinking the shaft is the miners' own work; working what comes out of it is not. Ore leaves
        // as bar, and bar needs a forge and the charcoal that feeds one.
        SettlementSpecialization.Mining => MiningCrafts,

        // A market moves goods in casks. The port's shipwright wants a town, see above.
        SettlementSpecialization.Trade => TradeCrafts,

        // A craft town is a cloth town before it is anything else: the weaver is the commonest
        // guild in every world, and the one the dyer and fuller wait on.
        SettlementSpecialization.Crafts => CraftCrafts,

        // A garrison is fed by the road, but it is armed by the forge.
        SettlementSpecialization.Military => MilitaryCrafts,

        _ => Array.Empty<Craft>(),
    };

    private static readonly Craft[] MiningCrafts = { Craft.Smith, Craft.CharcoalBurner };
    private static readonly Craft[] TradeCrafts = { Craft.Cooper };
    private static readonly Craft[] CraftCrafts = { Craft.Weaver };
    private static readonly Craft[] MilitaryCrafts = { Craft.Smith };

    /// <summary>
    /// The share of a trade's own yield lost when none of its required crafts can be held.
    /// </summary>
    /// <remarks>
    /// <para><b>A drag, not a veto.</b> A quarry town whose masons died does not stop being a quarry
    /// town; it stops working well. So a shortfall scales the two terms the trade earns —
    /// <see cref="SiteCapacity"/> and <see cref="ImportReliance"/> — and never the land, which
    /// feeds the place whatever it is known for. A town missing half its crafts loses half this.</para>
    /// </remarks>
    public const double CraftShortfallPenalty = 0.40;

    public static string Label(SettlementSpecialization specialization) => specialization switch
    {
        SettlementSpecialization.Farming => "farming",
        SettlementSpecialization.Pastoral => "herding",
        SettlementSpecialization.Fishing => "fishing",
        SettlementSpecialization.Mining => "mining",
        SettlementSpecialization.Trade => "trade",
        SettlementSpecialization.Crafts => "craftwork",
        SettlementSpecialization.Shrine => "pilgrimage",
        SettlementSpecialization.Military => "military",
        _ => "nothing in particular",
    };
}
