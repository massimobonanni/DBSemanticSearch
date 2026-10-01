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
            throw new InvalidOperationException("EMBEDDING_ENDPOINT must be a valid HTTPS URL.");
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("EMBEDDING_DEPLOYMENT is required.");
        if (!int.TryParse(dimensionValue, out var dimensions) || dimensions is < 1 or > 3072)
            throw new InvalidOperationException("EMBEDDING_DIMENSIONS must be between 1 and 3072.");

        var authMode = FoundryAuthenticationMode.DefaultAzureCredential;
        if (!string.IsNullOrWhiteSpace(authModeValue)
            && (!Enum.TryParse(authModeValue, ignoreCase: true, out authMode) || !Enum.IsDefined(authMode)))
            throw new InvalidOperationException(
                "EMBEDDING_AUTH_MODE must be DefaultAzureCredential, ManagedIdentity, or ApiKey.");
        if (authMode == FoundryAuthenticationMode.ApiKey && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("EMBEDDING_API_KEY is required when EMBEDDING_AUTH_MODE is ApiKey.");

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
