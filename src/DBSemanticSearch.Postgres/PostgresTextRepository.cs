using System.Globalization;
using DBSemanticSearch.Core.Entites;
using DBSemanticSearch.Core.Interfaces;
using Npgsql;
using NpgsqlTypes;

namespace DBSemanticSearch.Postgres;

public sealed class PostgresTextRepository(NpgsqlDataSource dataSource, int dimensions) : ITextRepository
{
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaReady;

    public async Task<TextDocument> InsertAsync(string text, float[] embedding, CancellationToken cancellationToken)
    {
        var vector = FormatVector(embedding);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO documents (id, text, embedding) VALUES (@id, @text, @embedding::vector) RETURNING created_at",
            connection);
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("text", text);
        command.Parameters.Add("embedding", NpgsqlDbType.Text).Value = vector;
        var createdAt = (DateTime)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Il database non ha restituito la data di creazione."));
        return new(id, text, new DateTimeOffset(createdAt, TimeSpan.Zero));
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(float[] embedding, int limit, CancellationToken cancellationToken)
    {
        var vector = FormatVector(embedding);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, text, created_at, embedding <=> @embedding::vector AS distance
            FROM documents
            ORDER BY distance, id
            LIMIT @limit
            """, connection);
        command.Parameters.Add("embedding", NpgsqlDbType.Text).Value = vector;
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hits = new List<SearchHit>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var document = new TextDocument(reader.GetGuid(0), reader.GetString(1),
                new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero));
            hits.Add(new(document, reader.GetDouble(3)));
        }
        return hits;
    }

    private string FormatVector(float[] embedding)
    {
        if (embedding.Length != dimensions || embedding.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Le dimensioni o i valori dell'embedding non sono validi.");

        return $"[{string.Join(",", embedding.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))}]";
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady) return;
        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady) return;
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using (var create = new NpgsqlCommand(
                $"""
                 CREATE EXTENSION IF NOT EXISTS vector;
                 CREATE TABLE IF NOT EXISTS documents (
                     id uuid PRIMARY KEY,
                     text text NOT NULL,
                     embedding vector({dimensions}) NOT NULL,
                     created_at timestamptz NOT NULL DEFAULT now()
                 );
                 """, connection))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var check = new NpgsqlCommand(
                "SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = 'documents'::regclass AND attname = 'embedding'",
                connection);
            var type = (string?)await check.ExecuteScalarAsync(cancellationToken);
            if (type != $"vector({dimensions})")
                throw new InvalidOperationException(
                    $"La colonna embedding è {type}, ma il modello configurato richiede vector({dimensions}).");
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }
}
