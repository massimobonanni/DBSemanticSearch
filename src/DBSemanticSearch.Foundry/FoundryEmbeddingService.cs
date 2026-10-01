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
                ?? throw new InvalidOperationException("The Foundry API key is not configured."))),
        FoundryAuthenticationMode.ManagedIdentity => new AzureOpenAIClient(settings.Endpoint,
            CreateManagedIdentityCredential(settings.ManagedIdentityClientId)),
        FoundryAuthenticationMode.DefaultAzureCredential => new AzureOpenAIClient(settings.Endpoint,
            new DefaultAzureCredential()),
        _ => throw new InvalidOperationException($"Unsupported authentication mode: {settings.AuthenticationMode}.")
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
            throw new InvalidOperationException("The returned embedding does not match the configured dimensions or contains invalid values.");
        return vector;
    }
}
