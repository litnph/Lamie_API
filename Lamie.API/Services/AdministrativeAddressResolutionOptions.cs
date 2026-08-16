namespace Lamie.API.Services;

public sealed class AdministrativeAddressResolutionOptions
{
    public const string SectionName = "AdministrativeAddressResolution";

    public string DefaultProvinceCode { get; init; } = "79";
    public int CandidateLimit { get; init; } = 8;
    public decimal ConfidentThreshold { get; init; } = 0.82m;
}
