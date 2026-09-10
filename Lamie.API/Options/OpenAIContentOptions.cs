namespace Lamie.API.Options;

public sealed class OpenAIContentOptions
{
    public const string SectionName = "OpenAIContent";

    public string ApiKey { get; init; } = string.Empty;
    public string Endpoint { get; init; } = "https://api.openai.com/v1/responses";
    public string Model { get; init; } = "gpt-5.6";
    public string PromptVersion { get; init; } = "lamie-social-content-v1";
    public int TimeoutSeconds { get; init; } = 60;
    public int MaximumRetries { get; init; } = 2;
    public int MaximumImages { get; init; } = 4;
    public long MaximumImageBytes { get; init; } = 10 * 1024 * 1024;
}
