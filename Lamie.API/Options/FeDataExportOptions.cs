namespace Lamie.API.Options;

public sealed class FeDataExportOptions
{
    public const string SectionName = "FeDataExport";

    public string AllowedRoot { get; init; } = "../../FE_Lamie/public";
    public string TargetDirectory { get; init; } = "../../FE_Lamie/public/fe-data";
}
