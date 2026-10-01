# DBSemanticSearch.Api

Azure Functions (.NET isolated worker, Functions v4) HTTP API for the DB Semantic Search solution. It exposes endpoints to store texts and to run semantic searches over them. Each text is converted into an embedding through Microsoft Foundry and persisted in PostgreSQL with `pgvector`.

The project is the composition root of the backend: it reads the configuration, wires the services through dependency injection, and translates HTTP requests into calls to `TextService`.

## Project structure

| File | Description |
| --- | --- |
| [`Program.cs`](Program.cs) | Host bootstrap. Registers `EmbeddingSettings`, the `NpgsqlDataSource`, `PostgresTextRepository`, `FoundryEmbeddingService`, and `TextService`, and selects the PostgreSQL authentication mode. |
| [`TextFunctions.cs`](TextFunctions.cs) | HTTP-triggered functions. Reads the request body (max 1 MiB), deserializes it into the `DBSemanticSearch.Contracts` request records, and maps results and errors to JSON responses. |
| [`BatchProcessor.cs`](BatchProcessor.cs) | Processes batch insertions item by item, so an invalid item or a failed embedding does not stop the other items. |
| [`BatchTextsConverter.cs`](BatchTextsConverter.cs) | JSON converter for the `texts` array. Keeps track of non-string items so they can be reported as per-item errors instead of failing the whole request. |
| [`host.json`](host.json) | Functions host configuration. |
| [`local.settings.example.json`](local.settings.example.json) | Template for `local.settings.json` used in local development. |
| [`Properties/AssemblyInfo.cs`](Properties/AssemblyInfo.cs) | Exposes internal types to the test project. |

### Dependencies

- [`DBSemanticSearch.Contracts`](../DBSemanticSearch.Contracts): request and response records shared with `DBSemanticSearch.Client` and the web front end.
- [`DBSemanticSearch.Core`](../DBSemanticSearch.Core): `TextService`, validation rules, and the `ITextRepository` / `IEmbeddingService` abstractions.
- [`DBSemanticSearch.Foundry`](../DBSemanticSearch.Foundry): embedding generation.
- [`DBSemanticSearch.Postgres`](../DBSemanticSearch.Postgres): persistence and vector search.

## API

All endpoints:

- use `POST` and accept and return `application/json`;
- serialize JSON with web defaults (camelCase, case-insensitive property names);
- require a Functions key (`AuthorizationLevel.Function`), passed in the `x-functions-key` header or the `code` query parameter;
- are exposed under the default `/api` route prefix.

### Text validation

Texts are validated by `TextService.Validate`:

- a text cannot be `null`, empty, or whitespace only;
- a text cannot exceed 10,000 characters;
- leading and trailing whitespace is trimmed before storage and search.

### `POST /api/texts`

Stores a single text and its embedding.

Request (`AddTextRequest`):

```json
{ "text": "The quick brown fox jumps over the lazy dog" }
```

Response `201 Created` (`TextDto`):

```json
{
  "id": "0b0f3c3e-2a5e-4b1f-9a51-1e6f0a2b7c11",
  "text": "The quick brown fox jumps over the lazy dog",
  "createdAt": "2026-10-01T09:30:00+00:00"
}
```

### `POST /api/texts/batch`

Stores up to 100 texts in one call. Each item is processed independently: invalid items and items whose embedding or insertion fails are reported with an error, while the others are stored.

Request (`TextBatchRequest`):

```json
{ "texts": ["first text", null, 42, "second text"] }
```

Response `200 OK` (`BatchResponse`):

```json
{
  "inserted": 2,
  "items": [
    { "index": 0, "document": { "id": "…", "text": "first text", "createdAt": "…" }, "error": null },
    { "index": 1, "document": null, "error": "Text cannot be empty." },
    { "index": 2, "document": null, "error": "The item must be a string." },
    { "index": 3, "document": { "id": "…", "text": "second text", "createdAt": "…" }, "error": null }
  ]
}
```

- `inserted` is the number of stored items.
- `items` contains one entry per input item, in input order; `index` is the zero-based position in the `texts` array.
- Exactly one of `document` and `error` is set. Unexpected failures (for example, an embedding service error) return the generic message `Unable to process the text. Please try again later.` and are logged.

The whole request is rejected with `400 Bad Request` when `texts` is missing, is not an array, or contains fewer than 1 or more than 100 items.

### `POST /api/search`

Returns the 5 stored texts closest to the query, ordered by increasing cosine distance.

Request (`SearchRequest`):

```json
{ "text": "a fast animal" }
```

Response `200 OK` (`SearchResponse`):

```json
{
  "results": [
    {
      "document": { "id": "…", "text": "The quick brown fox jumps over the lazy dog", "createdAt": "…" },
      "distance": 0.18
    }
  ]
}
```

`distance` is the `pgvector` cosine distance: lower values mean more similar texts.

### Errors

Errors are returned as `ErrorResponse`:

```json
{ "error": "The request body is required." }
```

| Status | When |
| --- | --- |
| `400 Bad Request` | Empty body or `null` body (`The request body is required.`), malformed JSON or wrong property types, failed text validation, invalid batch shape. |
| `401 Unauthorized` | Missing or invalid Functions key. |
| `413 Request Entity Too Large` | Body larger than 1 MiB (`The request exceeds 1 MiB.`). |
| `500 Internal Server Error` | Unhandled failure on single-text endpoints, such as the embedding service or the database being unavailable. |

## Configuration

| Variable | Description |
| --- | --- |
| `FUNCTIONS_WORKER_RUNTIME` | Must be `dotnet-isolated`. |
| `AzureWebJobsStorage` | Storage used by the Functions host (`UseDevelopmentStorage=true` locally). |
| `EMBEDDING_ENDPOINT`, `EMBEDDING_DEPLOYMENT`, `EMBEDDING_DIMENSIONS`, `EMBEDDING_AUTH_MODE` | Embedding model settings; see [`DBSemanticSearch.Foundry`](../DBSemanticSearch.Foundry/README.md). |
| `DB_CONNECTION_STRING` or `DB_HOST`, `DB_NAME`, `DB_USER`, `DB_AUTH_MODE`, `DB_PASSWORD`, `DB_MANAGED_IDENTITY_CLIENT_ID` | PostgreSQL connection and authentication; see [`DBSemanticSearch.Postgres`](../DBSemanticSearch.Postgres/README.md#configuration). |

## Running locally

1. Copy `local.settings.example.json` to `local.settings.json` and replace the placeholder values.
2. Start Azurite (or configure a real storage account) and a PostgreSQL instance with the `vector` extension available.
3. Start the host from the project folder:

```powershell
func start
```

Example request (the key is not required when running locally):

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:7071/api/search `
    -ContentType 'application/json' -Body '{ "text": "a fast animal" }'
```

## Tests

The API logic is covered by the solution tests:

```powershell
dotnet test tests\DBSemanticSearch.Tests
```
