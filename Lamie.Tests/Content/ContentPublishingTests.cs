using System.Net;
using System.Text;
using Lamie.API.Options;
using Lamie.API.Services;
using Lamie.Application.Content;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lamie.Tests.Content;

public sealed class ContentPublishingTests
{
    [Fact]
    public void SavingDraft_PreservesFooterSnapshotAfterSettingChanges()
    {
        var now = new DateTime(2026, 8, 17, 3, 0, 0, DateTimeKind.Utc);
        var footer = new ContentFooterSetting(
            ContentPlatform.Facebook,
            "Footer cũ",
            "#lamie",
            true,
            now,
            null);
        var generation = CreateGeneration(now, footer.Content, footer.Hashtags);

        footer.Update("Footer mới", "#new", true, now.AddMinutes(1), null);
        generation.Save(
            generation.Items.ToDictionary(item => item.Platform, item => item.Body + " đã sửa"),
            now.AddMinutes(2));

        var facebook = generation.Items.Single(item => item.Platform == ContentPlatform.Facebook);
        Assert.Equal(ContentGenerationStatus.Saved, generation.Status);
        Assert.Equal("Footer cũ", facebook.FooterSnapshot);
        Assert.Equal("#lamie", facebook.HashtagsSnapshot);
        Assert.Contains("Footer cũ", facebook.FullContent);
        Assert.DoesNotContain("Footer mới", facebook.FullContent);
    }

    [Fact]
    public void Generation_RequiresExactlyThreeDistinctPlatforms()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => new ContentGeneration(
            ContentSourceType.Product,
            1,
            "Hoa",
            null,
            null,
            "style",
            "Style",
            1,
            "fake",
            "fake-model",
            "v1",
            null,
            null,
            null,
            null,
            now,
            [
                new(ContentPlatform.Facebook, "A", null, null),
                new(ContentPlatform.Instagram, "B", null, null)
            ]));
    }

    [Fact]
    public async Task OpenAiProvider_UsesResponsesStructuredOutputAndParsesAllPlatforms()
    {
        string? requestJson = null;
        Uri? requestUri = null;
        var handler = new StubHttpHandler(async request =>
        {
            requestUri = request.RequestUri;
            requestJson = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """
                {
                  "status": "completed",
                  "output": [{
                    "type": "message",
                    "content": [{
                      "type": "output_text",
                      "text": "{\"facebook\":\"Bài Facebook\",\"instagram\":\"Bài Instagram\",\"tiktok\":\"Bài TikTok\"}"
                    }]
                  }]
                }
                """);
        });
        var provider = CreateProvider(handler);

        var result = await provider.GenerateAsync(
            new ContentAiRequest(
                new ContentAiProductContext(1, "LAM-1", "Hoa hồng", "Mô tả", 450000, null, "Hoa bó"),
                "Quà sinh nhật",
                "premium-minimal",
                "Tối giản cao cấp",
                42,
                [new ContentAiImage("flower.png", "image/png", [0x89, 0x50])]),
            CancellationToken.None);

        Assert.Equal("Bài Facebook", result.Facebook);
        Assert.Equal("Bài Instagram", result.Instagram);
        Assert.Equal("Bài TikTok", result.TikTok);
        Assert.NotNull(requestJson);
        Assert.Equal("https://api.openai.com/v1/responses", requestUri?.AbsoluteUri);
        Assert.Contains("\"type\":\"json_schema\"", requestJson);
        Assert.Contains("\"store\":false", requestJson);
        Assert.Contains("\"type\":\"input_image\"", requestJson);
        Assert.Contains("data:image/png;base64", requestJson);
        Assert.DoesNotContain("test-key", requestJson);
        Assert.DoesNotContain("Footer cũ", requestJson);
    }

    [Fact]
    public async Task OpenAiProvider_RetriesTransientFailureWithinBound()
    {
        var calls = 0;
        var handler = new StubHttpHandler(_ =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? Json(HttpStatusCode.TooManyRequests, "{}")
                : Json(HttpStatusCode.OK, """
                    {"status":"completed","output_text":"{\"facebook\":\"A\",\"instagram\":\"B\",\"tiktok\":\"C\"}"}
                    """));
        });
        var provider = CreateProvider(handler);

        var result = await provider.GenerateAsync(
            new ContentAiRequest(
                new ContentAiProductContext(1, "SKU", "Name", "Description", 1, null, null),
                null,
                "style",
                "Style",
                1,
                []),
            CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal("A", result.Facebook);
    }

    [Fact]
    public async Task OpenAiProvider_RejectsMissingOrDuplicatedStructuredFields()
    {
        var handler = new StubHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, """
            {"status":"completed","output_text":"{\"facebook\":\"Same\",\"instagram\":\"Same\",\"tiktok\":\"Same\"}"}
            """)));
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<ContentAiProviderException>(() => provider.GenerateAsync(
            new ContentAiRequest(
                new ContentAiProductContext(1, "SKU", "Name", "Description", 1, null, null),
                null,
                "style",
                "Style",
                1,
                []),
            CancellationToken.None));
    }

    [Fact]
    public async Task OpenAiProvider_MapsSyntacticallyValidWrongShapeToProviderError()
    {
        var handler = new StubHttpHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, "[]")));
        var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<ContentAiProviderException>(() => provider.GenerateAsync(
            new ContentAiRequest(
                new ContentAiProductContext(1, "SKU", "Name", "Description", 1, null, null),
                null,
                "style",
                "Style",
                1,
                []),
            CancellationToken.None));

        Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiProvider_MapsPerAttemptTimeoutToTemporaryProviderError()
    {
        var provider = new OpenAIContentAiProvider(
            new HttpClient(new CancelingHttpHandler()),
            Options.Create(new OpenAIContentOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://api.openai.com/v1/responses",
                Model = "gpt-5.6",
                MaximumRetries = 0,
                TimeoutSeconds = 1
            }));

        var exception = await Assert.ThrowsAsync<ContentAiProviderException>(() => provider.GenerateAsync(
            new ContentAiRequest(
                new ContentAiProductContext(1, "SKU", "Name", "Description", 1, null, null),
                null,
                "style",
                "Style",
                1,
                []),
            CancellationToken.None));

        Assert.True(exception.IsTemporary);
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiProvider_ReportsMissingServerConfigurationWithoutHttpCall()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("HTTP must not run"));
        var provider = new OpenAIContentAiProvider(
            new HttpClient(handler),
            Options.Create(new OpenAIContentOptions { ApiKey = string.Empty }));

        await Assert.ThrowsAsync<ContentAiNotConfiguredException>(() => provider.GenerateAsync(
            new ContentAiRequest(null, null, "style", "Style", 1, []),
            CancellationToken.None));
    }

    private static ContentGeneration CreateGeneration(
        DateTime now,
        string? footer,
        string? hashtags) =>
        new(
            ContentSourceType.Product,
            1,
            "Hoa hồng",
            "/uploads/rose.jpg",
            "Brief",
            "premium-minimal",
            "Tối giản cao cấp",
            7,
            "fake",
            "fake-model",
            "v1",
            null,
            null,
            null,
            null,
            now,
            [
                new(ContentPlatform.Facebook, "Facebook body", footer, hashtags),
                new(ContentPlatform.Instagram, "Instagram body", null, null),
                new(ContentPlatform.TikTok, "TikTok body", null, null)
            ]);

    private static OpenAIContentAiProvider CreateProvider(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new OpenAIContentOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://api.openai.com/v1/responses",
                Model = "gpt-5.6",
                MaximumRetries = 2,
                TimeoutSeconds = 5
            }));

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }

    private sealed class CancelingHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, "{}");
        }
    }
}
