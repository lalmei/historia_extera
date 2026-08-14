using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// The making of things worth writing down.
/// </summary>
/// <remarks>
/// <para><b>This system only makes them.</b> Everything that happens to an artifact afterwards is
/// written by whatever is already happening: an army sacks the town holding it, a settlement is
/// abandoned with it inside, a mountain buries it. Those live in <see cref="Treasures"/> and are
/// called from the systems that cause them, because "the Crown of Aigionanvos was carried off"
/// is a fact about a war, not about a yearly artifact tick.</para>
///
/// <para><b>What a place makes is what a place is.</b> A shrine produces relics, a craft town
/// produces work in gold and ink, a mining town produces weapons, and a capital with a king on
/// the throne produces regalia. Specialization has fed capacity since M4 and site choice since
/// M1; this is the first thing that turns it into objects, so a chronicle of a mining city reads
/// differently from one of a holy city in a way a reader notices without being told the rule.</para>
///
/// <para>Runs last in the year, after the houses: a thing made in the reign of a ruler crowned
/// this spring should be attributed to them rather than to whoever the year began with.</para>
///
/// <para>Samples no terrain.</para>
/// </remarks>
public sealed class ArtifactSystem : IYearSystem
{
    /// <summary>Yearly chance a qualifying settlement makes something, before its character.</summary>
    private const double CreationChance = 0.0022;

    /// <summary>How many objects one settlement can be famous for at once.</summary>
    private const int TreasuryLimit = 3;

    public string Name => "artifacts";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                // Below a town there is neither the wealth to commission anything nor a chronicle
                // that would have recorded it.
                if (settlement.Tier < SettlementTier.Town) continue;

                double chance = CreationChance * Appetite(settlement, culture);
                if (chance <= 0.0 || !rng.Chance(chance)) continue;

                // A treasury of twenty is a warehouse, not a legend. The cap is what keeps the
                // handful of famous objects in a world actually famous.
                if (Treasures.HeldBy(world, settlement.Id).Count >= TreasuryLimit) continue;

                ArtifactKind kind = Choose(world, settlement, civilization, rng);

                EntityId creator = kind == ArtifactKind.Regalia
                    ? civilization.CurrentRulerId
                    : EntityId.None;

                EntityId faith = kind is ArtifactKind.Relic or ArtifactKind.Idol
                    ? settlement.ReligionId
                    : EntityId.None;

                Treasures.Create(world, settlement, kind, creator, faith, year);
            }
        }
    }

    /// <summary>How much a settlement's character and culture incline it to make things.</summary>
    private static double Appetite(Settlement settlement, Culture culture)
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

        // Tradition makes a people keep and celebrate what it makes; piety pays for the rest.
        double values = 0.6 + (culture.Values.Tradition * 0.6) + (culture.Values.Piety * 0.4);

        return craft * size * seat * values;
    }

    /// <summary>
    /// What this place would make.
    /// </summary>
    /// <remarks>
    /// Decided by the settlement rather than rolled freely, so the object explains where it came
    /// from. The only randomness is between things a place could plausibly produce.
    /// </remarks>
    private static ArtifactKind Choose(
        WorldState world, Settlement settlement, Civilization civilization, IRng rng)
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

        return settlement.Specialization switch
        {
            SettlementSpecialization.Mining => rng.Chance(0.55) ? ArtifactKind.Weapon : ArtifactKind.Jewel,
            SettlementSpecialization.Crafts => rng.Chance(0.5) ? ArtifactKind.Jewel : ArtifactKind.Tome,
            SettlementSpecialization.Trade => rng.Chance(0.5) ? ArtifactKind.Jewel : ArtifactKind.Tome,
            _ when devout && rng.Chance(0.35) => ArtifactKind.Relic,

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
