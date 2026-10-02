# DBSemanticSearch.Foundry

Adapter that generates text embeddings with an Azure OpenAI deployment hosted in Microsoft Foundry (by default `text-embedding-3-large`).

## Purpose

The project implements the `IEmbeddingService` interface defined in `DBSemanticSearch.Core`. The application layer (`TextService`) calls it to turn each text into a vector before storing it or running a search. The rest of the solution does not depend on the Azure SDKs; they are only used inside this project.

Dependencies:

- `DBSemanticSearch.Core` for the `IEmbeddingService` contract
- `Azure.AI.OpenAI` for the embedding client
- `Azure.Identity` for Microsoft Entra ID credentials (managed identity / `DefaultAzureCredential`)

## Classes

| Class | Description |
| --- | --- |
| [`FoundryEmbeddingService`](FoundryEmbeddingService.cs) | Implements `IEmbeddingService`. Builds an `AzureOpenAIClient` with the credential selected by `AuthenticationMode` and gets the embedding client for the configured deployment. `GenerateAsync` returns the vector and throws `InvalidOperationException` if its length differs from `Dimensions` or it contains non-finite values. |
| [`EmbeddingSettings`](EmbeddingSettings.cs) | Immutable record with endpoint, deployment, dimensions, authentication mode, API key and optional managed identity client ID. `FromEnvironment()` reads and validates the environment variables described below. `ToString()` does not include the API key, so the settings can be logged safely. |
| [`FoundryAuthenticationMode`](FoundryAuthenticationMode.cs) | Enum of the supported credentials: `DefaultAzureCredential`, `ManagedIdentity`, `ApiKey`. |

## Configuration

`EmbeddingSettings.FromEnvironment()` reads these variables. On startup, any missing or invalid value throws `InvalidOperationException`.

| Variable | Required | Description |
| --- | --- | --- |
| `EMBEDDING_ENDPOINT` | Yes | HTTPS endpoint of the resource, e.g. `https://<resource>.openai.azure.com/` |
| `EMBEDDING_DEPLOYMENT` | Yes | Name of the embedding model deployment, e.g. `text-embedding-3-large` |
| `EMBEDDING_DIMENSIONS` | Yes | Vector size, 1–3072 (`3072` for `text-embedding-3-large`). It must match the `vector(n)` column in PostgreSQL. |
| `EMBEDDING_AUTH_MODE` | No | `DefaultAzureCredential` (default), `ManagedIdentity` or `ApiKey`, case-insensitive |
| `EMBEDDING_API_KEY` | Only with `ApiKey` | Resource key |
| `EMBEDDING_MANAGED_IDENTITY_CLIENT_ID` | No | Client ID of a user-assigned managed identity. If omitted, the system-assigned identity is used. |

### Authentication modes

- **`DefaultAzureCredential`** tries the Azure credential chain: managed identity in Azure, and Azure CLI or Visual Studio sign-in on a developer machine. This is the right choice for local development (`az login`).
- **`ManagedIdentity`** uses only the managed identity of the host (for example the Function App). It avoids the credential chain probing and is the mode used by the Azure deployment in `infra/backend.bicep`.
- **`ApiKey`** authenticates with the resource key. Use it only when Entra ID is not available. Never commit the key: store it in Key Vault and reference it from the app settings, or keep it in the untracked `local.settings.json`.

For both Entra ID modes the identity needs the **Cognitive Services OpenAI User** role on the Foundry / Azure OpenAI resource.

### Example (`local.settings.json`)

```json
{
  "Values": {
    "EMBEDDING_ENDPOINT": "https://my-resource.openai.azure.com/",
    "EMBEDDING_DEPLOYMENT": "text-embedding-3-large",
    "EMBEDDING_DIMENSIONS": "3072",
    "EMBEDDING_AUTH_MODE": "DefaultAzureCredential"
  }
}
```

### Registration

The API registers the settings and the service as singletons (see `src/DBSemanticSearch.Api/Program.cs`):

```csharp
var settings = EmbeddingSettings.FromEnvironment();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IEmbeddingService, FoundryEmbeddingService>();
```
