namespace Lamie.Infrastructure.Options;

public sealed class LocalStorageOptions
{
    public const string SectionName = "LocalStorage";

    public string RootPath { get; init; } = "wwwroot/uploads";
    public string PublicBasePath { get; init; } = "/uploads";
}
