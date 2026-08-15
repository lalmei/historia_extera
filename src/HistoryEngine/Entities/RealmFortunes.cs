using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// How a realm's recent past sits on it, in four decaying measures.
/// </summary>
/// <remarks>
/// <para><b>Written by whichever system caused it, at the moment it happens</b> — the same
/// provenance rule <see cref="DeathCause"/> follows. The alternative is deriving a realm's mood
/// by walking the chronicle every year, which is both slower and a second opinion about what
/// happened: the war system already knows it lost a battle, and asking the event log to work it
/// out again is how the two come to disagree.</para>
///
/// <para><b>Weariness and Grievance are deliberately separate, and pull opposite ways.</b> Being
/// beaten exhausts a realm; being humiliated angers it. A model with one scalar has to choose
/// between "we lost, so we will never fight again" and "we lost, so we will fight until we have
/// it back", and both are wrong on their own. Grievance also fades far more slowly than
/// weariness, which is the whole difference between a war a realm has recovered from and a war it
/// has not forgiven.</para>
///
/// <para>All four are in [0, 1] and decay geometrically, so a defeat governs a decade and is a
/// footnote in three.</para>
/// </remarks>
public sealed class RealmFortunes
{
    /// <summary>Yearly retention for the fast-fading measures: a twelve-year half-life.</summary>
    /// <remarks>
    /// Written as the computed literal rather than as <c>Math.Pow(0.5, 1.0 / 12.0)</c>, because
    /// <c>Math.Pow</c> has no cross-runtime bit-exactness guarantee and this value multiplies into
    /// every realm's decisions every year — see <c>DeterminismGuardTests</c>.
    /// </remarks>
    private const double Retention = 0.9438743126816935;

    /// <summary>Yearly retention for <see cref="Grievance"/>: a twenty-five-year half-life.</summary>
    private const double GrievanceRetention = 0.9726549474122855;

    private const double BattleLost = 0.18;
    private const double BattleWon = 0.15;
    private const double OwnTownSacked = 0.28;
    private const double EnemyTownSacked = 0.12;
    private const double LandLostWeariness = 0.10;
    private const double LandLostGrievance = 0.30;
    private const double LandTakenTriumph = 0.20;

    /// <summary>How much regaining ground answers an old humiliation.</summary>
    private const double LandTakenRelief = 0.25;

    /// <summary>
    /// Calamity added per unit share of the realm's people lost at once.
    /// </summary>
    /// <remarks>
    /// Three, so a settlement losing a tenth of the realm's population to plague or fire adds
    /// nearly a third of the scale. Catastrophes in this engine are concentrated in one place and
    /// measured against the whole realm precisely so that the same death toll matters more to a
    /// small realm than to a large one.
    /// </remarks>
    private const double CalamityPerShare = 3.0;

    /// <summary>The realm has been bled and knows it. Damps aggression; inclines it to trade.</summary>
    public double Weariness { get; private set; }

    /// <summary>It has been hurt by something it cannot fight. Damps expansion; drives it to the temple.</summary>
    public double Calamity { get; private set; }

    /// <summary>It is going well, and everyone can feel it.</summary>
    public double Triumph { get; private set; }

    /// <summary>Ground lost and not recovered. The humiliation that outlives the exhaustion.</summary>
    public double Grievance { get; private set; }

    public void LostABattle() => Weariness = Raise(Weariness, BattleLost);

    public void WonABattle() => Triumph = Raise(Triumph, BattleWon);

    /// <summary>One of this realm's towns was put to the sack.</summary>
    public void TownSacked() => Weariness = Raise(Weariness, OwnTownSacked);

    /// <summary>This realm did the sacking.</summary>
    public void SackedATown() => Triumph = Raise(Triumph, EnemyTownSacked);

    public void LandLost()
    {
        Weariness = Raise(Weariness, LandLostWeariness);
        Grievance = Raise(Grievance, LandLostGrievance);
    }

    public void LandTaken()
    {
        Triumph = Raise(Triumph, LandTakenTriumph);
        Grievance = Math.Max(0.0, Grievance - LandTakenRelief);
    }

    /// <summary>
    /// People lost at once to something that cannot be fought — plague, disaster, famine.
    /// </summary>
    /// <remarks>
    /// Measured as a share of the realm rather than as a headcount, so a thousand dead is a
    /// catastrophe to a realm of eight thousand and an unhappy year to a realm of ninety.
    /// </remarks>
    public void Suffered(int lost, int realmPopulation)
    {
        if (lost <= 0 || realmPopulation <= 0) return;

        double share = (double)lost / realmPopulation;
        Calamity = Raise(Calamity, DetMath.Clamp01(share) * CalamityPerShare);
    }

    /// <summary>A year passes and the memory dims. Called once a year, before anything reads it.</summary>
    public void Fade()
    {
        Weariness *= Retention;
        Calamity *= Retention;
        Triumph *= Retention;
        Grievance *= GrievanceRetention;
    }

    private static double Raise(double current, double amount) =>
        DetMath.Clamp01(current + amount);

    public override string ToString() =>
        "weariness " + Two(Weariness)
        + ", calamity " + Two(Calamity)
        + ", triumph " + Two(Triumph)
        + ", grievance " + Two(Grievance);

    /// <summary>Invariant two-decimal formatting: a locale must not reach the debugger either.</summary>
    private static string Two(double value) =>
        value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
