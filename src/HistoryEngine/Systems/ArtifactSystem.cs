using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// The making of things worth writing down, and the circulation of written works.
/// </summary>
/// <remarks>
/// <para><b>Objects move with events; books spread with time.</b> Looting, abandonment and loss
/// still belong to the systems that cause them. Copying is different: it is slow ordinary work,
/// so this yearly tick lets an existing exemplar travel through realm, faith and trade networks.
/// A copy remains attached to the original work rather than becoming another famous artifact.</para>
///
/// <para><b>What a place makes is what a place is.</b> A shrine produces relics, a craft town
/// produces work in gold and ink, a mining town produces weapons, and a capital with a king on
/// the throne produces regalia. Specialization has fed capacity since M4 and site choice since
/// M1; this is the first thing that turns it into objects, so a chronicle of a mining city reads
/// differently from one of a holy city in a way a reader notices without being told the rule.</para>
///
/// <para>Runs last in each season, after the houses. Creation and circulation still happen only
/// in the opening season, so a thing made in the reign of a ruler crowned this spring is
/// attributed to them rather than to whoever the year began with. Estate settlement also runs
/// after later seasonal docket entries: a ruler killed by a plague in the final winter must not
/// remain the recorded owner merely because there is no following spring.</para>
///
/// <para>Samples no terrain.</para>
/// </remarks>
public sealed class ArtifactSystem : ISystem
{
    /// <summary>Yearly chance a qualifying settlement makes something, before its character.</summary>
    private const double CreationChance = 0.0022;

    /// <summary>How many objects one settlement can be famous for at once.</summary>
    private const int TreasuryLimit = 3;

    /// <summary>
    /// Baseline appetite, lowered from 0.6 by half of <see cref="ScholarlyAppetite"/>.
    /// </summary>
    /// <remarks>
    /// Keeps the mean across a world's realms where it was before Learning existed, so the dial
    /// changes who commissions and not how much gets commissioned.
    /// </remarks>
    private const double ScholarlyFloor = 0.475;

    /// <summary>Extra appetite for commissioning anything at all, at full Learning.</summary>
    /// <remarks>
    /// Deliberately the smaller half of what Learning does. A scholarly court is not chiefly a
    /// court that makes <em>more</em> things — it is one that makes different ones, which is
    /// <see cref="Choose"/>'s business. Turning the dial into a volume knob would give a learned
    /// realm more weapons and relics too, which is not what the dial means.
    /// </remarks>
    private const double ScholarlyAppetite = 0.25;

    /// <summary>Odds a town with no particular craft produces a book anyway, at full Learning.</summary>
    private const double ScholarlyBias = 0.5;

    public string Name => "artifacts";

    public Cadence Cadence => Cadence.Seasonal;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        // Annual creation and circulation stay on the opening step. Later seasonal ticks exist
        // solely to settle deaths resolved from the docket after that step.
        if (now.Day != 0)
        {
            Treasures.SettleEstates(world, year);
            return;
        }

        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            // Patronage is a decision of the court, so this is the realm's effective values: a
            // learned ruler commissions books his father would not have paid for, and a realm
            // in the middle of a plague commissions rather less of anything.
            CultureValues values = world.ValuesFor(civilization);

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                // Below a town there is neither the wealth to commission anything nor a chronicle
                // that would have recorded it.
                if (settlement.Tier < SettlementTier.Town) continue;

                double chance = CreationChance * Appetite(settlement, values);
                if (chance <= 0.0 || !rng.Chance(chance)) continue;

                // A treasury of twenty is a warehouse, not a legend. The cap is what keeps the
                // handful of famous objects in a world actually famous.
                if (Treasures.HeldBy(world, settlement.Id).Count >= TreasuryLimit) continue;

                ArtifactKind kind = Choose(world, settlement, civilization, values, rng);

                EntityId creator = kind == ArtifactKind.Regalia
                    ? civilization.CurrentRulerId
                    : EntityId.None;

                EntityId owner = LivingPatron(civilization, world);

                EntityId faith = kind is ArtifactKind.Relic or ArtifactKind.Idol
                    ? settlement.ReligionId
                    : EntityId.None;

                Treasures.Create(world, settlement, kind, creator, faith, year, owner);
            }
        }

        Tomes.Commission(world, year);
        Treasures.SettleEstates(world, year);
        Treasures.ExchangeGifts(world, year);
        Tomes.Distribute(world, year);
        Tomes.Revise(world, year);
    }

    private static EntityId LivingPatron(Civilization civilization, WorldState world)
    {
        if (civilization.CurrentRulerId.IsNone || !world.Figures.Contains(civilization.CurrentRulerId))
        {
            return EntityId.None;
        }

        Figure ruler = world.Figures[civilization.CurrentRulerId];
        return ruler.IsAlive ? ruler.Id : EntityId.None;
    }

    /// <summary>How much a settlement's character and its realm's inclinations incline it to make things.</summary>
    private static double Appetite(Settlement settlement, CultureValues values)
    {
        double craft = settlement.Specialization switch
        {
            SettlementSpecialization.Crafts => 2.6,
            SettlementSpecialization.Shrine => 2.2,
            SettlementSpecialization.Mining => 1.5,
            SettlementSpecialization.Trade => 1.3,
            _ => 0.7,
        };

        double size = settlement.Tier == SettlementTier.City ? 1.7 : 1.0;
        double seat = settlement.IsCapital ? 1.5 : 1.0;

        // Tradition makes a people keep and celebrate what it makes; piety pays for the rest; and
        // a learned court commissions work nobody had asked it for.
        //
        // The floor is lowered by half of Learning's mean contribution so that adding the dial
        // redistributes appetite rather than inflating it. A learned realm should make more than
        // an unlearned one, not more than the world used to make in total — the alternative
        // quietly raises every realm's artifact volume by a tenth, which is a calibration change
        // masquerading as a feature.
        double inclination = ScholarlyFloor
            + (values.Tradition * 0.6)
            + (values.Piety * 0.4)
            + (values.Learning * ScholarlyAppetite);

        return craft * size * seat * inclination;
    }

    /// <summary>
    /// What this place would make.
    /// </summary>
    /// <remarks>
    /// Decided by the settlement rather than rolled freely, so the object explains where it came
    /// from. The only randomness is between things a place could plausibly produce.
    /// </remarks>
    private static ArtifactKind Choose(
        WorldState world,
        Settlement settlement,
        Civilization civilization,
        CultureValues values,
        IRng rng)
    {
        bool devout = !settlement.ReligionId.IsNone && world.Religions.Contains(settlement.ReligionId);

        if (settlement.Specialization == SettlementSpecialization.Shrine && devout)
        {
            return rng.Chance(0.6) ? ArtifactKind.Relic : ArtifactKind.Idol;
        }

        // Regalia needs a crown to belong to, and a living head to put it on.
        if (settlement.IsCapital
            && !civilization.CurrentRulerId.IsNone
            && world.Figures.Contains(civilization.CurrentRulerId)
            && world.Figures[civilization.CurrentRulerId].IsAlive
            && rng.Chance(0.45))
        {
            return ArtifactKind.Regalia;
        }

        // Where a place could make either a fine object or a book, how learned its realm is
        // decides which. This is the larger half of what Learning does: a scholarly court is
        // known for what it wrote, not for how much it commissioned.
        double jewelOverTome = DetMath.Lerp(0.65, 0.30, values.Learning);

        return settlement.Specialization switch
        {
            SettlementSpecialization.Mining => rng.Chance(0.55) ? ArtifactKind.Weapon : ArtifactKind.Jewel,
            SettlementSpecialization.Crafts => rng.Chance(jewelOverTome) ? ArtifactKind.Jewel : ArtifactKind.Tome,
            SettlementSpecialization.Trade => rng.Chance(jewelOverTome) ? ArtifactKind.Jewel : ArtifactKind.Tome,
            _ when devout && rng.Chance(0.35) => ArtifactKind.Relic,

            // A learned realm writes things down even where there is no craft to speak of.
            _ when rng.Chance(values.Learning * ScholarlyBias) => ArtifactKind.Tome,

            // Everywhere else spreads across the three things any town can produce. Falling
            // through to a single kind made half the objects in the world books, because most
            // settlements farm and farming has no craft of its own.
            _ => Ordinary[rng.NextInt(Ordinary.Length)],
        };
    }

    /// <summary>What a town with no particular character makes. Fixed order, for reproducibility.</summary>
    private static readonly ArtifactKind[] Ordinary =
    {
        ArtifactKind.Tome,
        ArtifactKind.Jewel,
        ArtifactKind.Weapon,
    };
}
