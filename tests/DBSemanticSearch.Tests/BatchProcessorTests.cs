using System.Text.Json;
using DBSemanticSearch.Api;
using DBSemanticSearch.Contracts;
using DBSemanticSearch.Core.Entites;
using DBSemanticSearch.Core.Interfaces;
using DBSemanticSearch.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DBSemanticSearch.Tests;

public sealed class BatchProcessorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new BatchTextsConverter() }
    };

    [Fact]
    public async Task InvalidItemAndFailedEmbeddingDoNotPreventOtherItemsFromBeingSaved()
    {
        var request = JsonSerializer.Deserialize<TextBatchRequest>(
            """{"texts":["primo",null," ","fallisce",42,{"a":1},"ultimo"]}""", JsonOptions)!;
        var repository = new RecordingRepository();
        var texts = new TextService(repository, new FailingEmbedding());

        var response = await BatchProcessor.ProcessAsync(request.Texts, texts,
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, response.Inserted);
        Assert.Equal(7, response.Items.Count);
        Assert.Equal(new[] { "primo", "ultimo" }, repository.SavedTexts);
        Assert.Equal(1, response.Items[1].Index);
        Assert.NotNull(response.Items[1].Error);
        Assert.NotNull(response.Items[2].Error);
        Assert.NotNull(response.Items[3].Error);
        Assert.Equal("The item must be a string.", response.Items[4].Error);
        Assert.Equal("The item must be a string.", response.Items[5].Error);
        Assert.Equal("ultimo", response.Items[6].Document?.Text);
    }

    [Theory]
    [InlineData("""{"texts":[]}""")]
    [InlineData("{}")]
    public async Task RejectsWrongBatchShape(string json)
    {
        var request = JsonSerializer.Deserialize<TextBatchRequest>(json, JsonOptions)!;
        var texts = new TextService(new RecordingRepository(), new FailingEmbedding());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BatchProcessor.ProcessAsync(request.Texts, texts, NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public void RejectsNonArrayTexts()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TextBatchRequest>("""{"texts":"abc"}""", JsonOptions));
    }

    [Fact]
    public async Task RejectsMoreThanOneHundredItems()
    {
        var texts = new TextService(new RecordingRepository(), new FailingEmbedding());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BatchProcessor.ProcessAsync(Enumerable.Repeat<string?>("test", 101).ToList(), texts,
                NullLogger.Instance, CancellationToken.None));
    }

    private sealed class FailingEmbedding : IEmbeddingService
    {
        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken) =>
            text == "fallisce"
                ? throw new InvalidOperationException("Model error")
                : Task.FromResult(new float[] { 1, 2 });
    }

    private sealed class RecordingRepository : ITextRepository
    {
        public List<string> SavedTexts { get; } = [];

        public Task<TextDocument> InsertAsync(string text, float[] embedding, CancellationToken cancellationToken)
        {
            SavedTexts.Add(text);
            return Task.FromResult(new TextDocument(Guid.NewGuid(), text, DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<SearchHit>> SearchAsync(float[] embedding, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SearchHit>>([]);
    }
}
