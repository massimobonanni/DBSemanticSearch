using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using DBSemanticSearch.Core.Interfaces;

namespace DBSemanticSearch.Foundry;

public sealed class FoundryEmbeddingService : IEmbeddingService
{
    private readonly OpenAI.Embeddings.EmbeddingClient _client;
    private readonly int _dimensions;

    public FoundryEmbeddingService(EmbeddingSettings settings)
    {
        _client = CreateClient(settings).GetEmbeddingClient(settings.Deployment);
        _dimensions = settings.Dimensions;
    }

    private static AzureOpenAIClient CreateClient(EmbeddingSettings settings) => settings.AuthenticationMode switch
    {
        FoundryAuthenticationMode.ApiKey => new AzureOpenAIClient(settings.Endpoint,
            new ApiKeyCredential(settings.ApiKey
                ?? throw new InvalidOperationException("La chiave API di Foundry non è configurata."))),
        FoundryAuthenticationMode.ManagedIdentity => new AzureOpenAIClient(settings.Endpoint,
            CreateManagedIdentityCredential(settings.ManagedIdentityClientId)),
        FoundryAuthenticationMode.DefaultAzureCredential => new AzureOpenAIClient(settings.Endpoint,
            new DefaultAzureCredential()),
        _ => throw new InvalidOperationException($"Modalità di autenticazione non supportata: {settings.AuthenticationMode}.")
    };

    private static TokenCredential CreateManagedIdentityCredential(string? clientId) =>
        new ManagedIdentityCredential(string.IsNullOrWhiteSpace(clientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(clientId));

    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        var vector = result.Value.ToFloats().ToArray();
        if (vector.Length != _dimensions || vector.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("L'embedding restituito non corrisponde alle dimensioni configurate o contiene valori non validi.");
        return vector;
    }
}
