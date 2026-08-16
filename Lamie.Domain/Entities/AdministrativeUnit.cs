namespace Lamie.Domain.Entities;

public enum AdministrativeScheme
{
    Current = 1,
    Legacy = 2
}

public enum AdministrativeUnitType
{
    Province = 1,
    Municipality = 2,
    District = 3,
    UrbanDistrict = 4,
    Town = 5,
    ProvincialCity = 6,
    Commune = 7,
    Ward = 8,
    Township = 9,
    SpecialZone = 10
}

public enum AdministrativeTransitionType
{
    Same = 1,
    Renamed = 2,
    MergedInto = 3,
    SplitInto = 4,
    Partial = 5,
    Other = 6
}

public sealed class AdministrativeUnit
{
    private AdministrativeUnit()
    {
    }

    public AdministrativeUnit(
        string code,
        string name,
        string fullName,
        string normalizedName,
        AdministrativeScheme scheme,
        AdministrativeUnitType unitType,
        int hierarchyLevel,
        string? parentCode,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        bool isActive,
        string sourceDocument,
        string sourceReference,
        string datasetVersion,
        int sortOrder)
    {
        Id = Guid.NewGuid();
        Apply(code, name, fullName, normalizedName, scheme, unitType, hierarchyLevel, parentCode,
            effectiveFrom, effectiveTo, isActive, sourceDocument, sourceReference, datasetVersion, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public AdministrativeScheme Scheme { get; private set; }
    public AdministrativeUnitType UnitType { get; private set; }
    public int HierarchyLevel { get; private set; }
    public string? ParentCode { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public string SourceDocument { get; private set; } = string.Empty;
    public string SourceReference { get; private set; } = string.Empty;
    public string DatasetVersion { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    public void Update(
        string name,
        string fullName,
        string normalizedName,
        AdministrativeUnitType unitType,
        int hierarchyLevel,
        string? parentCode,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        bool isActive,
        string sourceDocument,
        string sourceReference,
        string datasetVersion,
        int sortOrder) =>
        Apply(Code, name, fullName, normalizedName, Scheme, unitType, hierarchyLevel, parentCode,
            effectiveFrom, effectiveTo, isActive, sourceDocument, sourceReference, datasetVersion, sortOrder);

    private void Apply(
        string code,
        string name,
        string fullName,
        string normalizedName,
        AdministrativeScheme scheme,
        AdministrativeUnitType unitType,
        int hierarchyLevel,
        string? parentCode,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        bool isActive,
        string sourceDocument,
        string sourceReference,
        string datasetVersion,
        int sortOrder)
    {
        Code = code.Trim();
        Name = name.Trim();
        FullName = fullName.Trim();
        NormalizedName = normalizedName.Trim();
        Scheme = scheme;
        UnitType = unitType;
        HierarchyLevel = hierarchyLevel;
        ParentCode = string.IsNullOrWhiteSpace(parentCode) ? null : parentCode.Trim();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
        SourceDocument = sourceDocument.Trim();
        SourceReference = sourceReference.Trim();
        DatasetVersion = datasetVersion.Trim();
        SortOrder = sortOrder;
    }
}

public sealed class AdministrativeUnitTransition
{
    private AdministrativeUnitTransition()
    {
    }

    public AdministrativeUnitTransition(
        string legacyUnitCode,
        string currentUnitCode,
        AdministrativeTransitionType transitionType,
        string sourceDocument,
        string sourceReference,
        DateOnly effectiveDate,
        string datasetVersion,
        string? note)
    {
        Id = Guid.NewGuid();
        Apply(legacyUnitCode, currentUnitCode, transitionType, sourceDocument, sourceReference,
            effectiveDate, datasetVersion, note);
    }

    public Guid Id { get; private set; }
    public string LegacyUnitCode { get; private set; } = string.Empty;
    public string CurrentUnitCode { get; private set; } = string.Empty;
    public AdministrativeTransitionType TransitionType { get; private set; }
    public string SourceDocument { get; private set; } = string.Empty;
    public string SourceReference { get; private set; } = string.Empty;
    public DateOnly EffectiveDate { get; private set; }
    public string DatasetVersion { get; private set; } = string.Empty;
    public string? Note { get; private set; }

    public void Update(
        AdministrativeTransitionType transitionType,
        string sourceDocument,
        string sourceReference,
        DateOnly effectiveDate,
        string datasetVersion,
        string? note) =>
        Apply(LegacyUnitCode, CurrentUnitCode, transitionType, sourceDocument, sourceReference,
            effectiveDate, datasetVersion, note);

    private void Apply(
        string legacyUnitCode,
        string currentUnitCode,
        AdministrativeTransitionType transitionType,
        string sourceDocument,
        string sourceReference,
        DateOnly effectiveDate,
        string datasetVersion,
        string? note)
    {
        LegacyUnitCode = legacyUnitCode.Trim();
        CurrentUnitCode = currentUnitCode.Trim();
        TransitionType = transitionType;
        SourceDocument = sourceDocument.Trim();
        SourceReference = sourceReference.Trim();
        EffectiveDate = effectiveDate;
        DatasetVersion = datasetVersion.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
