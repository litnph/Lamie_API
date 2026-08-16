using Lamie.Domain.Entities;

namespace Lamie.Application.Addresses;

public sealed record AdministrativeUnitDto(
    string Code,
    string Name,
    string FullName,
    AdministrativeScheme Scheme,
    AdministrativeUnitType UnitType,
    int HierarchyLevel,
    string? ParentCode,
    bool IsActive,
    int SortOrder);

public sealed record AdministrativeTransitionDto(
    string LegacyUnitCode,
    string CurrentUnitCode,
    AdministrativeTransitionType TransitionType,
    string CurrentUnitName,
    string? CurrentProvinceName,
    string SourceDocument,
    string SourceReference,
    string? Note,
    bool IsSuggestionOnly);

public sealed class ResolveAddressRequest
{
    public string Text { get; init; } = string.Empty;
    public AdministrativeScheme? PreferredScheme { get; init; }
    public string? DefaultProvinceCode { get; init; }
}

public sealed record AddressResolutionSuggestionDto(
    AdministrativeScheme Scheme,
    string? ProvinceCode,
    string? ProvinceName,
    string? DistrictCode,
    string? DistrictName,
    string? CommuneCode,
    string? CommuneName,
    string? AddressDetail,
    string FullAddress,
    decimal Confidence,
    string Reason,
    bool UsedDefaultProvince);

public sealed record AddressResolutionDto(
    string OriginalText,
    string NormalizedText,
    bool IsConfident,
    decimal Confidence,
    AddressResolutionSuggestionDto? SelectedCandidate,
    IReadOnlyList<AddressResolutionSuggestionDto> Candidates,
    IReadOnlyList<string> Warnings);
