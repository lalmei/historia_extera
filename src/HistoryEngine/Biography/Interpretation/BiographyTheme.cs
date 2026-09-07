namespace HistoryEngine.Biography.Interpretation;

/// <summary>Semantic biography themes produced by the interpretation pipeline.</summary>
public enum BiographyTheme
{
    Rootedness = 0,
    EnduringLoyalty = 1,
    ReligiousDevotion = 2,
    ScholarlyLife = 3,
    TransmissionOfKnowledge = 4,
    CommercialSuccess = 5,
    CommercialFailure = 6,
    TerritorialAmbition = 7,
    FrontierLife = 8,
    ViolentConflict = 9,
    ConsolidationOfPower = 10,
    ResistanceToAuthority = 11,
    Isolation = 12,
    InstitutionalService = 13,
    ReligiousScholarship = 14,
}

/// <summary>Broad domains used to suppress redundant interpretations.</summary>
public enum BiographyThemeGroup
{
    Identity = 0,
    Relationships = 1,
    Power = 2,
    Knowledge = 3,
    Faith = 4,
    Conflict = 5,
    Commerce = 6,
    Mobility = 7,
}

public static class BiographyThemeGroups
{
    public static BiographyThemeGroup Of(BiographyTheme theme) => theme switch
    {
        BiographyTheme.Rootedness or BiographyTheme.Isolation => BiographyThemeGroup.Identity,
        BiographyTheme.EnduringLoyalty => BiographyThemeGroup.Relationships,
        BiographyTheme.ConsolidationOfPower or BiographyTheme.ResistanceToAuthority
            or BiographyTheme.InstitutionalService or BiographyTheme.TerritorialAmbition
            => BiographyThemeGroup.Power,
        BiographyTheme.ScholarlyLife or BiographyTheme.TransmissionOfKnowledge
            or BiographyTheme.ReligiousScholarship
            => BiographyThemeGroup.Knowledge,
        BiographyTheme.ReligiousDevotion => BiographyThemeGroup.Faith,
        BiographyTheme.ViolentConflict => BiographyThemeGroup.Conflict,
        BiographyTheme.CommercialSuccess or BiographyTheme.CommercialFailure
            => BiographyThemeGroup.Commerce,
        BiographyTheme.FrontierLife => BiographyThemeGroup.Mobility,
        _ => BiographyThemeGroup.Identity,
    };
}
