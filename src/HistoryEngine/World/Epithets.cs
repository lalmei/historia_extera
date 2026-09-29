using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// The byname a ruler or a captain is remembered by: the Conqueror, the Pious, the Child.
/// </summary>
/// <remarks>
/// <para><b>Earned from the record, given at the grave.</b> A byname is posterity's verdict, and
/// it is decided once the life is over and what it held can be counted: the wars a reign won or
/// lost, how long it lasted, how old the heir was when crowned, how the ruler died. Character only
/// decides it when the deeds say nothing: a reign that won no war and lost none is remembered for
/// its temper instead, and only when that temper was extreme.</para>
///
/// <para><b>Most specific first.</b> A king who fell in battle is remembered for that before his
/// piety, and a conqueror for his wars before his age. The rules run from the facts a chronicle
/// would lead with down to the ones it reaches for when there is nothing else, and many reigns
/// end with no byname at all, which is also how history treats them.</para>
///
/// <para><b>Decides nothing.</b> Like <see cref="Ailments"/>, this is flavour. Nothing reads the
/// epithet back, and the words are chosen from a hash of the figure rather than from any stream,
/// so adding a byname to a list changes which byname a king gets and nothing else.</para>
/// </remarks>
public static class Epithets
{
    /// <summary>A byname and the clause that says why it was given.</summary>
    public readonly record struct Verdict(string Epithet, string Reason);

    /// <summary>
    /// Gives a byname to someone just buried, when their record earns one, and records it.
    /// </summary>
    public static void Consider(WorldState world, Figure figure, int year)
    {
        if (Judge(figure, year) is not { } verdict) return;

        figure.Epithet = verdict.Epithet;

        world.Chronicle.Record(
            year,
            EventKind.EpithetEarned,
            figure.Id,
            obj: figure.CivilizationId,
            data: Chronicle.Data(
                ("epithet", verdict.Epithet),
                ("styled", figure.FullName + " " + verdict.Epithet),
                ("reason", verdict.Reason)),
            significance: Significance.Notable);
    }

    /// <summary>The byname this record earns, or null when it earns none.</summary>
    internal static Verdict? Judge(Figure figure, int year)
    {
        int reign = YearsHeld(figure, OfficeKind.Ruler, year);
        bool ruled = figure.Offices.Exists(office => office.Kind == OfficeKind.Ruler);

        int warsWon = 0, warsLost = 0, fieldsWon = 0, fieldsLost = 0;
        foreach (CampaignMemory memory in figure.Campaigns)
        {
            if (memory.Role == CampaignRole.Ruled)
            {
                if (memory.Triumphant == true) warsWon++;
                else if (memory.Triumphant == false) warsLost++;
            }
            else if (memory.Role == CampaignRole.Commanded)
            {
                if (memory.Triumphant == true) fieldsWon++;
                else if (memory.Triumphant == false) fieldsLost++;
            }
        }

        if (!ruled)
        {
            // A captain gets a byname only for a record nobody else in the host could claim.
            if (fieldsWon >= 3 && fieldsLost == 0)
            {
                return Pick(figure, "for never losing a field",
                    "the Unbeaten", "the Hammer", "the Sure-Handed");
            }

            return null;
        }

        int age = figure.AgeIn(year);
        int crowned = FirstHeld(figure, OfficeKind.Ruler) - figure.BirthYear;
        CultureValues temper = figure.Disposition.Values;

        if (figure.DeathCause == DeathCause.Battle)
        {
            return Pick(figure, "for falling in battle", "the Valiant", "the Fallen", "the Brave");
        }

        if (figure.DeathCause is DeathCause.Assassination or DeathCause.Poisoning
            && temper.Piety >= 0.6)
        {
            return Pick(figure, "for a pious life ended by murder", "the Martyr");
        }

        if (warsWon >= 2 && warsLost == 0)
        {
            return warsWon >= 3
                ? Pick(figure, "for " + Count(warsWon) + " wars won and none lost",
                    "the Conqueror", "the Great", "the Victorious")
                : Pick(figure, "for two wars won and none lost", "the Victorious", "the Bold");
        }

        if (warsLost >= 2 && warsWon == 0)
        {
            return Pick(figure, "for " + Count(warsLost) + " wars lost and none won",
                "the Unlucky", "the Luckless", "the Ill-Starred");
        }

        if (crowned < 12 && age < 20)
        {
            return Pick(figure, "for a crown worn in childhood and lost young", "the Child", "the Young");
        }

        if (reign <= 1 && age >= 20)
        {
            return Pick(figure, "for a reign of " + (reign == 1 ? "a single year" : "months"),
                "the Brief", "the Short-Lived");
        }

        if (age >= 75)
        {
            return Pick(figure, "for living to " + age.ToString(CultureInfo.InvariantCulture),
                "the Old", "the Grey", "the Long-Lived");
        }

        if (reign >= 40)
        {
            return Pick(figure, "for a reign of " + reign.ToString(CultureInfo.InvariantCulture) + " years",
                "the Enduring", "the Long-Reigning");
        }

        if (warsWon + warsLost == 0 && reign >= 15 && temper.Aggression <= 0.2)
        {
            return Pick(figure, "for " + reign.ToString(CultureInfo.InvariantCulture) + " years without a war",
                "the Peaceable", "the Peacemaker");
        }

        // Temper, when the wars and the years say nothing — and only with a deed behind it. A
        // byname is what people saw a ruler do; a pious king who never made a pilgrimage was pious
        // where nobody could see it, and posterity does not name what it did not see.
        int pilgrimages = figure.Journeys.Count(journey => journey.Kind == JourneyKind.Pilgrimage);
        if (temper.Piety >= 0.8 && pilgrimages >= 1)
        {
            return Pick(figure,
                pilgrimages >= 2 ? "for " + Count(pilgrimages) + " pilgrimages made" : "for a pilgrimage made in state",
                "the Pious", "the Devout");
        }

        if (temper.Learning >= 0.8 && (figure.Claims.Count > 0 || figure.Occupation == Occupation.Scribe
                || figure.PriorOccupation == Occupation.Scribe))
        {
            return Pick(figure,
                figure.Claims.Count > 0 ? "for reading the sky" : "for a scholar's learning",
                "the Learned", "the Wise");
        }

        if (temper.Aggression >= 0.85 && warsWon + warsLost + fieldsWon + fieldsLost >= 1)
        {
            return Pick(figure, "for a warlike temper", "the Fierce", "the Hard");
        }

        if (temper.Tradition >= 0.85 && reign >= 20)
        {
            return Pick(figure, "for " + reign.ToString(CultureInfo.InvariantCulture) + " years holding to the old ways",
                "the Steadfast");
        }

        return null;
    }

    private static Verdict Pick(Figure figure, string reason, params string[] names)
    {
        uint mix = unchecked((uint)figure.Id.ToDiscriminator() * Narration.VariantMix);
        return new Verdict(names[(int)(mix % (uint)names.Length)], reason);
    }

    private static int FirstHeld(Figure figure, OfficeKind kind)
    {
        int first = int.MaxValue;
        foreach (OfficeHolding office in figure.Offices)
        {
            if (office.Kind == kind) first = Math.Min(first, office.FromYear);
        }

        return first;
    }

    private static int YearsHeld(Figure figure, OfficeKind kind, int year)
    {
        int years = 0;
        foreach (OfficeHolding office in figure.Offices)
        {
            if (office.Kind == kind) years += (office.ToYear ?? year) - office.FromYear;
        }

        return years;
    }

    private static string Count(int n) => n switch
    {
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };
}
