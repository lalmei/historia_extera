using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>One return of a named comet, in a year, at a brightness.</summary>
/// <remarks>
/// Derived from the orbit rolled at world creation, never stored and never rolled again. Two runs of
/// the same seed produce the same apparitions in the same years whatever happens in the history,
/// because the sky does not care what the history is doing — which is the property the whole idea
/// rests on. It is also what lets a later prediction be checked against something the simulation
/// knew before anybody was born.
/// </remarks>
public sealed record Apparition(int CometIndex, int Year, ApparitionGrade Grade);

/// <summary>
/// What the sky did, and who was standing under it with a pen.
/// </summary>
/// <remarks>
/// <para><b>The sky is rolled, not invented.</b> M17 placed real comets on real orbits and left them
/// as flavour in the export. Their periods are the interesting part: seed 11 carries one on 74.8
/// years and one on 160.7, so a 300-year run sees the first four times and the second twice. That is
/// long enough for the same object to be seen by people who never met, which is the only reason an
/// interval can be noticed at all.</para>
///
/// <para><b>Observation is not universal.</b> An apparition happens whether or not anyone looks. A
/// realm records it only if somebody there keeps records — a scribe, or a priesthood — and the odds
/// read that realm's learning, the brightness, and whether it was at war that year. Two realms
/// watching the same comet produce two records under two names, which is the shape a later
/// disagreement needs.</para>
///
/// <para>This class does not decide what an apparition <em>means</em>. It records that a person, in a
/// town, in a year, saw something, and how long it had been since the last time their realm saw the
/// same thing. What anyone concludes from that interval is a separate question.</para>
/// </remarks>
public static class Skywatch
{
    /// <summary>
    /// Below this a return is not worth a line, whoever was watching.
    /// </summary>
    /// <remarks>
    /// Brightness stands in for apparent magnitude: a big nucleus close to the star is a spectacle
    /// and a small one that never comes inside the world's own orbit is not. Seed 11 rolls four
    /// comets and this excludes exactly one of them — the 4.8-year visitor whose perihelion is
    /// outside the world's orbit — which is the right answer. A comet that returned sixty-two times
    /// in three centuries is weather, and a chronicle that recorded it would be a worse chronicle.
    /// </remarks>
    private const double VisibleBrightness = 6.0;

    private const double NotableBrightness = 20.0;

    private const double GreatBrightness = 60.0;

    /// <summary>
    /// A faint comet has to be rare to be worth a line.
    /// </summary>
    /// <remarks>
    /// Brightness alone is not enough, and seed 7 is why: it rolls a faint comet on a ten-year
    /// period, which under a brightness-only rule returned thirty times in three centuries and was
    /// written down a hundred and forty-eight times. Nobody chronicles a thing they saw three times
    /// before they were grown. Halley is famous because it is bright <em>and</em> comes once a
    /// lifetime; Encke comes every three years and only astronomers know its name. A bright comet
    /// still earns its line at any period — a spectacle is a spectacle — but a faint one has to be
    /// rare enough that most people who see it are seeing it for the first time.
    /// </remarks>
    private const double MinFaintPeriodYears = 25.0;

    /// <summary>Realms with nobody who keeps records never look up, however bright it was.</summary>
    private const double BaseRecordChance = 0.30;

    // -----------------------------------------------------------------------
    // What the sky does
    // -----------------------------------------------------------------------

    /// <summary>Every return of every visible comet within the run, in year then comet order.</summary>
    public static List<Apparition> Apparitions(WorldState world) =>
        Apparitions(world.Flavour.Cosmology, world.StartYear, world.EndYear);

    /// <summary>Every return of every chronicled comet in a span, in year then comet order.</summary>
    public static List<Apparition> Apparitions(WorldCosmology sky, int startYear, int endYear)
    {
        var found = new List<Apparition>();

        foreach (SystemComet comet in sky.Comets)
        {
            if (!Chronicled(sky, comet, out ApparitionGrade grade, out double period)) continue;

            double first = startYear + (Phase(comet) * period);
            for (double at = first; at <= endYear; at += period)
            {
                int year = (int)Math.Round(at);
                if (year < startYear || year > endYear) continue;

                found.Add(new Apparition(comet.Index, year, grade));
            }
        }

        found.Sort(static (a, b) =>
        {
            int byYear = a.Year.CompareTo(b.Year);
            return byYear != 0 ? byYear : a.CometIndex.CompareTo(b.CometIndex);
        });

        return found;
    }

    /// <summary>
    /// Whether this comet is the sort of thing a register would carry at all, and how bright.
    /// </summary>
    /// <remarks>
    /// The single place that decides it, so the list of apparitions and the yearly pass that
    /// records them cannot come to different answers about the same sky.
    /// </remarks>
    public static bool Chronicled(
        WorldCosmology sky, SystemComet comet, out ApparitionGrade grade, out double periodYears)
    {
        grade = ApparitionGrade.Faint;
        periodYears = 0.0;

        if (sky.OrbitalPeriodDays <= 0.0) return false;

        double brightness = Brightness(comet);
        if (brightness < VisibleBrightness) return false;

        periodYears = PeriodYears(sky, comet);
        if (periodYears <= 0.0) return false;

        grade = Grade(brightness);
        return grade > ApparitionGrade.Faint || periodYears >= MinFaintPeriodYears;
    }

    /// <summary>
    /// Where in its orbit the comet was when the history opened.
    /// </summary>
    /// <remarks>
    /// The argument of periapsis is already rolled per comet and is uncorrelated between them, so it
    /// serves as the phase without adding a field to the cosmology or drawing another number: two
    /// comets on similar periods do not arrive together, and no comet arrives in year one because
    /// that is when the chronicle happens to start.
    /// </remarks>
    private static double Phase(SystemComet comet)
    {
        double phase = (comet.ArgumentOfPeriapsisRad / (2.0 * Math.PI)) % 1.0;
        return phase < 0.0 ? phase + 1.0 : phase;
    }

    /// <summary>How long this comet takes to come back, in this world's own years.</summary>
    public static double PeriodYears(WorldCosmology sky, SystemComet comet) =>
        sky.OrbitalPeriodDays <= 0.0 ? 0.0 : comet.OrbitalPeriodDays / sky.OrbitalPeriodDays;

    /// <summary>
    /// A stand-in for apparent magnitude, in the only two terms the roll gives us.
    /// </summary>
    /// <remarks>
    /// Brightness falls with the square of the distance at closest approach and rises with the size
    /// of the thing catching the light. That is not photometry, but it separates a great comet from
    /// a faint one on the numbers already rolled, which is all this needs to do.
    /// </remarks>
    public static double Brightness(SystemComet comet) =>
        comet.PerihelionAu <= 0.0
            ? 0.0
            : comet.NucleusRadiusKm / (comet.PerihelionAu * comet.PerihelionAu);

    private static ApparitionGrade Grade(double brightness) => brightness switch
    {
        >= GreatBrightness => ApparitionGrade.Great,
        >= NotableBrightness => ApparitionGrade.Notable,
        _ => ApparitionGrade.Faint,
    };

    // -----------------------------------------------------------------------
    // Who was watching
    // -----------------------------------------------------------------------

    /// <summary>Records this year's returns, in whichever realms had anyone to record them.</summary>
    public static void Record(WorldState world, int year)
    {
        WorldCosmology sky = world.Flavour.Cosmology;
        if (sky.OrbitalPeriodDays <= 0.0) return;

        foreach (SystemComet comet in sky.Comets)
        {
            if (!ReturnsThisYear(sky, comet, year, world.StartYear, out ApparitionGrade grade))
            {
                continue;
            }

            foreach (Civilization civilization in world.ActiveCivilizations())
            {
                Watch(world, civilization, comet, grade, year);
            }
        }
    }

    private static bool ReturnsThisYear(
        WorldCosmology sky,
        SystemComet comet,
        int year,
        int startYear,
        out ApparitionGrade grade)
    {

        if (!Chronicled(sky, comet, out grade, out double period)) return false;

        // Which return, if any, rounds to this year. Compared on the rounded year rather than a
        // tolerance so that the answer here and the list in Apparitions can never disagree.
        double first = startYear + (Phase(comet) * period);
        double turns = (year - first) / period;
        for (int n = (int)Math.Floor(turns) - 1; n <= (int)Math.Ceiling(turns) + 1; n++)
        {
            if (n < 0) continue;
            if ((int)Math.Round(first + (n * period)) == year) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether this realm wrote it down, and who held the pen.
    /// </summary>
    /// <remarks>
    /// Forked on the comet, the year and the realm, so whether one realm saw something cannot move
    /// with the founding of another. The observer is chosen rather than drawn: the person in the
    /// realm most given to letters is the one who records it, which makes the record follow the
    /// realm's actual learning rather than a second roll.
    /// </remarks>
    private static void Watch(
        WorldState world,
        Civilization civilization,
        SystemComet comet,
        ApparitionGrade grade,
        int year)
    {
        Figure? watcher = Recorder(world, civilization, year);
        if (watcher is null) return;

        double chance = BaseRecordChance
            + (world.ValuesFor(civilization).Learning * 0.34)
            + (watcher.Disposition.Values.Learning * 0.20)
            + grade switch
            {
                ApparitionGrade.Great => 0.24,
                ApparitionGrade.Notable => 0.10,
                _ => 0.0,
            };

        // A realm fighting for its life is not keeping a register of the sky.
        if (Fighting(world, civilization)) chance -= 0.22;

        IRng fate = world.Root
            .Fork("skywatch", comet.Index)
            .Fork("year", year)
            .Fork("realm", civilization.Id.ToDiscriminator());
        if (!fate.Chance(DetMath.Clamp01(chance))) return;

        int? prior = LastSeenBy(world, civilization, comet.Index, year);
        EntityId place = world.ResidenceOf(watcher);

        watcher.Observations.Add(
            new SkyObservation(comet.Index, year, civilization.Id, place, prior, grade));

        LifeStories.Remember(
            watcher,
            MemoryKind.Wonder,
            year,
            EventKind.ApparitionRecorded,
            location: place,
            intensity: grade switch
            {
                ApparitionGrade.Great => 0.82,
                ApparitionGrade.Notable => 0.62,
                _ => 0.45,
            });

        var data = new DetMap<string, string>
        {
            ["grade"] = GradePhrase(grade),
            ["comet"] = comet.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (prior is int seen)
        {
            data["since"] = (year - seen).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        world.Chronicle.Record(
            year,
            EventKind.ApparitionRecorded,
            watcher.Id,
            obj: civilization.Id,
            location: place,
            data: data,
            significance: grade == ApparitionGrade.Great
                ? Significance.Notable
                : Significance.Routine);
    }

    /// <summary>
    /// The person in this realm who would have written it down.
    /// </summary>
    /// <remarks>
    /// A realm with no scribe and no priesthood keeps no register, and that is a real difference
    /// between realms rather than an oversight: whether an apparition survives into the record at all
    /// depends on somebody's trade, which is the same reason half of what we know about real comets
    /// comes from monasteries.
    /// </remarks>
    private static Figure? Recorder(WorldState world, Civilization civilization, int year)
    {
        Figure? best = null;
        double bestLearning = -1.0;

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != civilization.Id) continue;
            if (figure.AgeIn(year) < Succession.MajorityAge) continue;
            if (figure.Occupation is not (Occupation.Scribe or Occupation.Clergy)) continue;
            if (!world.Settlements.Contains(world.ResidenceOf(figure))) continue;

            double learning = figure.Disposition.Values.Learning;
            if (learning > bestLearning
                || (learning == bestLearning && best is not null && figure.Id.CompareTo(best.Id) < 0))
            {
                best = figure;
                bestLearning = learning;
            }
        }

        return best;
    }

    /// <summary>
    /// The last year this realm recorded this same body, if it ever did.
    /// </summary>
    /// <remarks>
    /// The interval is the whole point of keeping the prior year rather than deriving it later. A
    /// scribe knows how long it has been only if their own realm's register goes back that far, and a
    /// realm that lost its records has to start counting again — which is a fact about that realm's
    /// continuity, and exactly the fact a later claim will stand or fall on.
    /// </remarks>
    private static int? LastSeenBy(
        WorldState world, Civilization civilization, int cometIndex, int before)
    {
        int? last = null;
        foreach (Figure figure in world.Figures)
        {
            foreach (SkyObservation seen in figure.Observations)
            {
                // The realm on the record, not the realm the writer ended up in.
                if (seen.RealmId != civilization.Id) continue;
                if (seen.CometIndex != cometIndex || seen.Year >= before) continue;
                if (last is null || seen.Year > last) last = seen.Year;
            }
        }

        return last;
    }

    private static bool Fighting(WorldState world, Civilization civilization)
    {
        foreach (War war in world.ActiveWars())
        {
            if (war.Involves(civilization.Id)) return true;
        }

        return false;
    }

    public static string GradePhrase(ApparitionGrade grade) => grade switch
    {
        ApparitionGrade.Great => "a great comet",
        ApparitionGrade.Notable => "a comet",
        _ => "a faint comet",
    };
}
