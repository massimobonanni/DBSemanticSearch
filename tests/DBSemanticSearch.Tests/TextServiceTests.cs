using DBSemanticSearch.Core.Entites;
using DBSemanticSearch.Core.Interfaces;
using DBSemanticSearch.Core.Services;

namespace DBSemanticSearch.Tests;

public sealed class TextServiceTests
{
    [Fact]
    public async Task AddStoresNormalizedTextWithEmbedding()
    {
        var repository = new FakeRepository();
        var embedding = new FakeEmbedding();
        var service = new TextService(repository, embedding);

        var saved = await service.AddAsync("  esempio  ");

        Assert.Equal("esempio", saved.Text);
        Assert.Equal("esempio", embedding.LastText);
        Assert.Equal(new float[] { 1, 2 }, repository.LastVector);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task RejectsEmptyInput(string? text)
    {
        var service = new TextService(new FakeRepository(), new FakeEmbedding());
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(text));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(text));
    }

    [Fact]
    public async Task RejectsTextLongerThanLimit()
    {
        var service = new TextService(new FakeRepository(), new FakeEmbedding());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddAsync(new string('a', TextService.MaxTextLength + 1)));
    }

    [Fact]
    public async Task SearchUsesSameModelAndRequestsFiveClosestDocuments()
    {
        var repository = new FakeRepository();
        var embedding = new FakeEmbedding();
        var service = new TextService(repository, embedding);
        await service.AddAsync("primo");

        var results = await service.SearchAsync("  ricerca  ");

        Assert.Single(results);
        Assert.Equal("ricerca", embedding.LastText);
        Assert.Equal(TextService.SearchLimit, repository.LastLimit);
        Assert.Equal(new float[] { 1, 2 }, repository.LastVector);
    }

    private sealed class FakeEmbedding : IEmbeddingService
    {
        public string? LastText { get; private set; }

        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken)
        {
            LastText = text;
            return Task.FromResult(new float[] { 1, 2 });
        }
    }

    private sealed class FakeRepository : ITextRepository
    {
        private readonly List<TextDocument> _documents = [];
        public float[]? LastVector { get; private set; }
        public int LastLimit { get; private set; }

        public Task<TextDocument> InsertAsync(string text, float[] embedding, CancellationToken cancellationToken)
        {
            LastVector = embedding;
            var saved = new TextDocument(Guid.NewGuid(), text, DateTimeOffset.UtcNow);
            _documents.Add(saved);
            return Task.FromResult(saved);
        }

        public Task<IReadOnlyList<SearchHit>> SearchAsync(float[] embedding, int limit, CancellationToken cancellationToken)
        {
            LastVector = embedding;
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<SearchHit>>(_documents
                .Take(limit).Select(document => new SearchHit(document, 0)).ToArray());
        }
    }
}
