using System.Text;

namespace DBSemanticSearch.Foundry;

public sealed record EmbeddingSettings(
    Uri Endpoint,
    string Deployment,
    int Dimensions,
    FoundryAuthenticationMode AuthenticationMode = FoundryAuthenticationMode.DefaultAzureCredential,
    string? ApiKey = null,
    string? ManagedIdentityClientId = null)
{
    public static EmbeddingSettings FromEnvironment()
    {
        var endpoint = Environment.GetEnvironmentVariable("EMBEDDING_ENDPOINT");
        var deployment = Environment.GetEnvironmentVariable("EMBEDDING_DEPLOYMENT");
        var dimensionValue = Environment.GetEnvironmentVariable("EMBEDDING_DIMENSIONS");
        var authModeValue = Environment.GetEnvironmentVariable("EMBEDDING_AUTH_MODE");
        var apiKey = Environment.GetEnvironmentVariable("EMBEDDING_API_KEY");
        var clientId = Environment.GetEnvironmentVariable("EMBEDDING_MANAGED_IDENTITY_CLIENT_ID");

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("EMBEDDING_ENDPOINT deve essere un URL HTTPS valido.");
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("EMBEDDING_DEPLOYMENT è obbligatorio.");
        if (!int.TryParse(dimensionValue, out var dimensions) || dimensions is < 1 or > 2000)
            throw new InvalidOperationException("EMBEDDING_DIMENSIONS deve essere compreso tra 1 e 2000.");

        var authMode = FoundryAuthenticationMode.DefaultAzureCredential;
        if (!string.IsNullOrWhiteSpace(authModeValue)
            && (!Enum.TryParse(authModeValue, ignoreCase: true, out authMode) || !Enum.IsDefined(authMode)))
            throw new InvalidOperationException(
                "EMBEDDING_AUTH_MODE deve essere DefaultAzureCredential, ManagedIdentity o ApiKey.");
        if (authMode == FoundryAuthenticationMode.ApiKey && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("EMBEDDING_API_KEY è obbligatorio quando EMBEDDING_AUTH_MODE è ApiKey.");

        return new(uri, deployment, dimensions, authMode,
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            string.IsNullOrWhiteSpace(clientId) ? null : clientId);
    }

    // Excludes the API key from ToString() to avoid leaking it in logs.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Endpoint = {Endpoint}, Deployment = {Deployment}, Dimensions = {Dimensions}, ");
        builder.Append($"AuthenticationMode = {AuthenticationMode}, ManagedIdentityClientId = {ManagedIdentityClientId}");
        return true;
    }
}
