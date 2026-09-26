using System.Text.Json;
using DBSemanticSearch.Api;
using DBSemanticSearch.Core.Entites;
using DBSemanticSearch.Core.Interfaces;
using DBSemanticSearch.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DBSemanticSearch.Tests;

public sealed class BatchProcessorTests
{
    [Fact]
    public async Task InvalidItemAndFailedEmbeddingDoNotPreventOtherItemsFromBeingSaved()
    {
        using var document = JsonDocument.Parse("""["primo",null," ","fallisce","ultimo"]""");
        var repository = new RecordingRepository();
        var texts = new TextService(repository, new FailingEmbedding());

        var response = await BatchProcessor.ProcessAsync(document.RootElement, texts,
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, response.Inserted);
        Assert.Equal(5, response.Items.Count);
        Assert.Equal(new[] { "primo", "ultimo" }, repository.SavedTexts);
        Assert.Equal(1, response.Items[1].Index);
        Assert.NotNull(response.Items[1].Error);
        Assert.NotNull(response.Items[2].Error);
        Assert.NotNull(response.Items[3].Error);
        Assert.Equal("ultimo", response.Items[4].Document?.Text);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task RejectsWrongBatchShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        var texts = new TextService(new RecordingRepository(), new FailingEmbedding());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BatchProcessor.ProcessAsync(document.RootElement, texts, NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsMoreThanOneHundredItems()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Enumerable.Repeat("test", 101)));
        var texts = new TextService(new RecordingRepository(), new FailingEmbedding());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BatchProcessor.ProcessAsync(document.RootElement, texts, NullLogger.Instance, CancellationToken.None));
    }

    private sealed class FailingEmbedding : IEmbeddingService
    {
        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken) =>
            text == "fallisce"
                ? throw new InvalidOperationException("Errore modello")
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
