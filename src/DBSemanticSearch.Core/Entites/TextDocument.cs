namespace DBSemanticSearch.Core.Entites;

public sealed record TextDocument(Guid Id, string Text, DateTimeOffset CreatedAt);
