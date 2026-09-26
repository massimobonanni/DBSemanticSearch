namespace DBSemanticSearch.Contracts;

public sealed record AddTextRequest(string? Text);
public sealed record TextBatchRequest(IReadOnlyList<string?> Texts);
public sealed record SearchRequest(string? Text);
public sealed record TextDto(Guid Id, string Text, DateTimeOffset CreatedAt);
public sealed record BatchItemResult(int Index, TextDto? Document, string? Error);
public sealed record BatchResponse(int Inserted, IReadOnlyList<BatchItemResult> Items);
public sealed record SearchResult(TextDto Document, double Distance);
public sealed record SearchResponse(IReadOnlyList<SearchResult> Results);
public sealed record ErrorResponse(string Error);
