using DBSemanticSearch.Core.Entites;

namespace DBSemanticSearch.Core.Interfaces;

public interface ITextRepository
{
    Task<TextDocument> InsertAsync(string text, float[] embedding, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchHit>> SearchAsync(float[] embedding, int limit, CancellationToken cancellationToken);

    Task DeleteAllAsync(CancellationToken cancellationToken);
}
