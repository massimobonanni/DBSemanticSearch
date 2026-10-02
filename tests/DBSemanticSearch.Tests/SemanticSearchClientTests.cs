using System.Net;
using System.Text;
using System.Text.Json;
using DBSemanticSearch.Client;

namespace DBSemanticSearch.Tests;

public sealed class SemanticSearchClientTests
{
    [Fact]
    public async Task BatchSendsJsonUnchangedAndReadsPartialResults()
    {
        const string json = """{"texts":["primo",null,"secondo"]}""";
        var handler = new FakeHandler(async request =>
        {
            Assert.Equal("/api/texts/batch", request.RequestUri!.AbsolutePath);
            Assert.Equal(json, await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"inserted":2,"items":[{"index":0,"document":{"id":"00000000-0000-0000-0000-000000000001","text":"primo","createdAt":"2026-01-01T00:00:00Z"},"error":null},{"index":1,"document":null,"error":"invalid item"},{"index":2,"document":{"id":"00000000-0000-0000-0000-000000000002","text":"secondo","createdAt":"2026-01-01T00:00:00Z"},"error":null}]}""",
                    Encoding.UTF8, "application/json")
            };
        });
        var client = new SemanticSearchClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") });

        var result = await client.AddBatchJsonAsync(json);

        Assert.Equal(2, result.Inserted);
        Assert.Equal("invalid item", result.Items[1].Error);
    }

    [Fact]
    public async Task ErrorResponseIsExposed()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"Text cannot be empty."}""", Encoding.UTF8, "application/json")
        }));
        var client = new SemanticSearchClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(""));
        Assert.Contains("Text cannot be empty", exception.Message);
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task DeleteAllSendsDeleteAndAcceptsEmptyResponse()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/api/texts", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-functions-key")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        httpClient.DefaultRequestHeaders.Add("x-functions-key", "test-key");
        var client = new SemanticSearchClient(httpClient);

        await client.DeleteAllAsync();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":\"Unauthorized.\"}", "Unauthorized.")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":\"Unable to delete the texts.\"}", "Unable to delete the texts.")]
    [InlineData(HttpStatusCode.BadGateway, "Service unavailable", "invalid service response")]
    public async Task DeleteAllExposesErrors(HttpStatusCode status, string body, string expectedMessage)
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new SemanticSearchClient(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.DeleteAllAsync());

        Assert.Contains(expectedMessage, exception.Message);
        Assert.Equal(status, exception.StatusCode);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
