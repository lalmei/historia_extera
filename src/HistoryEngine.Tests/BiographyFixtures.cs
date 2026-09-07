using System.Text.Json;
using System.Text.Json.Serialization;
using HistoryEngine.Biography;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.Tests;

internal sealed class BiographyFixtureFile
{
    public List<BiographyFixtureCase> Cases { get; set; } = new();
}

internal sealed class BiographyFixtureCase
{
    public string Name { get; set; } = "";

    public int Year { get; set; }

    public BiographyFixtureFigure Figure { get; set; } = new();

    public List<string> Expected { get; set; } = new();
}

internal sealed class BiographyFixtureFigure
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Sex { get; set; } = "Female";

    public int BirthYear { get; set; }

    public string Occupation { get; set; } = "None";

    public BiographyFixtureDisposition Disposition { get; set; } = new();

    public List<BiographyFixtureResidence> Residences { get; set; } = new();

    public List<BiographyFixtureAffinity> Affinities { get; set; } = new();

    public List<BiographyFixtureObservation> Observations { get; set; } = new();
}

internal sealed class BiographyFixtureDisposition
{
    public double Aggression { get; set; } = 0.5;

    public double Expansionism { get; set; } = 0.5;

    public double Piety { get; set; } = 0.5;

    public double Tradition { get; set; } = 0.5;

    public double Mercantile { get; set; } = 0.5;

    public double Learning { get; set; } = 0.5;

    public double Centralism { get; set; } = 0.5;

    public double Independence { get; set; } = 0.5;
}

internal sealed class BiographyFixtureResidence
{
    public int SettlementId { get; set; }

    public int FromYear { get; set; }

    public string Reason { get; set; } = "Birth";
}

internal sealed class BiographyFixtureAffinity
{
    public int Id { get; set; }

    public int OtherId { get; set; }

    public int StartYear { get; set; }

    public string Stage { get; set; } = "Friendship";

    public string Origin { get; set; } = "SharedResidence";

    public int PlaceId { get; set; }
}

internal sealed class BiographyFixtureObservation
{
    public int CometIndex { get; set; }

    public int Year { get; set; }

    public string Grade { get; set; } = "Notable";

    public int? PriorYear { get; set; }
}

internal static class BiographyFixtures
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath => Path.GetFullPath(
        Path.Combine(EngineSource.Root, "..", "..", "testdata", "biography-signatures.json"));

    public static IReadOnlyList<BiographyFixtureCase> Load()
    {
        string json = File.ReadAllText(FilePath);
        BiographyFixtureFile file = JsonSerializer.Deserialize<BiographyFixtureFile>(json, Options)
            ?? throw new InvalidOperationException($"Empty biography fixture at {FilePath}.");
        return file.Cases;
    }

    public static Figure Hydrate(BiographyFixtureFigure spec)
    {
        Sex sex = Enum.Parse<Sex>(spec.Sex);
        Occupation occupation = Enum.Parse<Occupation>(spec.Occupation);
        var figure = new Figure(
            EntityId.Figure(spec.Id),
            EntityId.Civilization(0),
            EntityId.Culture(0),
            spec.Name,
            sex,
            spec.BirthYear)
        {
            Occupation = occupation,
            Disposition = new Disposition(
                new CultureValues(
                    spec.Disposition.Aggression,
                    spec.Disposition.Expansionism,
                    spec.Disposition.Piety,
                    spec.Disposition.Tradition,
                    spec.Disposition.Mercantile,
                    spec.Disposition.Learning),
                spec.Disposition.Centralism,
                spec.Disposition.Independence),
        };

        foreach (BiographyFixtureResidence residence in spec.Residences)
        {
            figure.Residences.Add(new Residence(
                EntityId.Settlement(residence.SettlementId),
                residence.FromYear,
                Enum.Parse<ResidenceReason>(residence.Reason)));
        }

        foreach (BiographyFixtureAffinity affinitySpec in spec.Affinities)
        {
            var affinity = new FigureAffinity(
                affinitySpec.Id,
                figure.Id,
                EntityId.Figure(affinitySpec.OtherId),
                affinitySpec.StartYear,
                Enum.Parse<AffinityOrigin>(affinitySpec.Origin),
                EventKind.FigureBorn,
                figure.Id,
                EntityId.Settlement(affinitySpec.PlaceId));
            affinity.Stage = Enum.Parse<AffinityStage>(affinitySpec.Stage);
            affinity.Acts.Add(new AffinityAct(
                affinitySpec.StartYear,
                EventKind.FigureBorn,
                affinity.Stage,
                figure.Id,
                "friendship"));
            figure.Affinities.Add(affinity);
        }

        foreach (BiographyFixtureObservation seen in spec.Observations)
        {
            figure.Observations.Add(new SkyObservation(
                seen.CometIndex,
                seen.Year,
                EntityId.Civilization(0),
                EntityId.Settlement(1),
                seen.PriorYear,
                Enum.Parse<ApparitionGrade>(seen.Grade)));
        }

        return figure;
    }

    public static IReadOnlyList<string> Signatures(BiographyFixtureCase fixture) =>
        BiographySignatures.For(new BiographyContext(Hydrate(fixture.Figure), fixture.Year, id => id.ToString()));
}
