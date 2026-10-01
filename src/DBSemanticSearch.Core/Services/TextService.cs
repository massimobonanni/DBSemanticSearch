using DBSemanticSearch.Core.Entites;
using DBSemanticSearch.Core.Interfaces;

namespace DBSemanticSearch.Core.Services;

public sealed class TextService(ITextRepository repository, IEmbeddingService embeddings)
{
    public const int MaxTextLength = 10_000;
    public const int SearchLimit = 5;

    public async Task<TextDocument> AddAsync(string? text, CancellationToken cancellationToken = default)
    {
        var normalized = Validate(text);
        var vector = await embeddings.GenerateAsync(normalized, cancellationToken);
        return await repository.InsertAsync(normalized, vector, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string? text, CancellationToken cancellationToken = default)
    {
        var normalized = Validate(text);
        var vector = await embeddings.GenerateAsync(normalized, cancellationToken);
        return await repository.SearchAsync(vector, SearchLimit, cancellationToken);
    }

    public static string Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty.");

        if (text.Length > MaxTextLength)
            throw new ArgumentException($"Text cannot exceed {MaxTextLength} characters.");

        return text.Trim();
    }
}
