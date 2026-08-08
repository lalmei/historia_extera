namespace HistoryEngine.Entities;

/// <summary>
/// How a house decides who inherits. Explicit values — part of the export format.
/// </summary>
/// <remarks>
/// Each law is a different traversal of the same family tree, which is why they are worth having
/// as data rather than as a single hard-coded rule: the tree is the expensive part, and one realm
/// passing the throne to the eldest son while its neighbour passes it to the eldest brother
/// produces two visibly different chronicles from identical machinery.
/// </remarks>
public enum SuccessionLaw
{
    /// <summary>Male line only. Daughters neither inherit nor transmit a claim.</summary>
    Agnatic = 0,

    /// <summary>Sons before daughters, but a daughter inherits before an uncle.</summary>
    MalePreference = 1,

    /// <summary>Eldest child, sex disregarded.</summary>
    Absolute = 2,

    /// <summary>The eldest of the house — brothers before sons.</summary>
    Seniority = 3,

    /// <summary>Chosen from among the claimants, so the strongest claim usually but not always wins.</summary>
    Elective = 4,
}

/// <summary>Which law a culture follows, and for how long its rulers hold office.</summary>
public static class SuccessionLaws
{
    /// <summary>
    /// The law implied by a culture's government and values.
    /// </summary>
    /// <remarks>
    /// A pure function rather than a roll at founding, so a culture's law can be read off its
    /// government and traits instead of being an unexplained fact about it — and so adding a
    /// government form later cannot shift every unrelated random draw in the run.
    ///
    /// <para><see cref="CultureValues.Tradition"/> chooses among the monarchical laws: a people
    /// deeply attached to precedent keeps the crown in the male line, and one indifferent to it
    /// hands the throne to the eldest child whoever that is.</para>
    /// </remarks>
    public static SuccessionLaw For(GovernmentForm government, CultureValues values) => government switch
    {
        GovernmentForm.Chiefdom => SuccessionLaw.Seniority,
        GovernmentForm.Theocracy => SuccessionLaw.Elective,
        GovernmentForm.Oligarchy => SuccessionLaw.Elective,
        GovernmentForm.Republic => SuccessionLaw.Elective,
        _ => values.Tradition > 0.66
            ? SuccessionLaw.Agnatic
            : values.Tradition > 0.33
                ? SuccessionLaw.MalePreference
                : SuccessionLaw.Absolute,
    };

    /// <summary>
    /// Years a ruler holds office before standing down, or zero for a reign that lasts for life.
    /// </summary>
    /// <remarks>
    /// The one place government form changes the <em>rhythm</em> of a chronicle rather than just
    /// its vocabulary. A republic that elects a consul every eight years produces four times the
    /// successions of a monarchy over the same span, and its houses trade the office back and
    /// forth instead of holding it.
    /// </remarks>
    public static int TermYears(GovernmentForm government) => government switch
    {
        GovernmentForm.Republic => 8,
        GovernmentForm.Oligarchy => 15,
        _ => 0,
    };

    public static string Label(SuccessionLaw law) => law switch
    {
        SuccessionLaw.Agnatic => "the male line",
        SuccessionLaw.MalePreference => "male-preference primogeniture",
        SuccessionLaw.Absolute => "primogeniture",
        SuccessionLaw.Seniority => "seniority",
        SuccessionLaw.Elective => "election",
        _ => "custom",
    };
}
