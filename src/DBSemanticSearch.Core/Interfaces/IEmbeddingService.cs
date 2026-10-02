namespace DBSemanticSearch.Core.Interfaces;

public interface IEmbeddingService
{
    Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken);
}
