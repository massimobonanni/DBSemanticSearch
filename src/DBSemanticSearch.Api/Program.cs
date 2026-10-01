using Azure.Core;
using Azure.Identity;
using DBSemanticSearch.Core.Interfaces;
using DBSemanticSearch.Core.Services;
using DBSemanticSearch.Foundry;
using DBSemanticSearch.Postgres;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var builder = FunctionsApplication.CreateBuilder(args);
var settings = EmbeddingSettings.FromEnvironment();

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(CreatePostgresDataSource());
builder.Services.AddSingleton<ITextRepository>(serviceProvider =>
    new PostgresTextRepository(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(),
        settings.Dimensions));
builder.Services.AddSingleton<IEmbeddingService, FoundryEmbeddingService>();
builder.Services.AddSingleton<TextService>();
builder.Build().Run();

static NpgsqlDataSource CreatePostgresDataSource()
{
    var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(connectionString))
        return NpgsqlDataSource.Create(connectionString);

    var host = Environment.GetEnvironmentVariable("DB_HOST");
    var user = Environment.GetEnvironmentVariable("DB_USER");
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
        throw new InvalidOperationException("Configure DB_CONNECTION_STRING or both DB_HOST and DB_USER.");

    var authMode = Environment.GetEnvironmentVariable("DB_AUTH_MODE") ?? "Password";
    var connectionStringBuilder = new NpgsqlConnectionStringBuilder
    {
        Host = host,
        Database = Environment.GetEnvironmentVariable("DB_NAME") ?? "semantic_search",
        Username = user,
        SslMode = SslMode.VerifyFull
    };

    if (authMode.Equals("Password", StringComparison.OrdinalIgnoreCase))
    {
        connectionStringBuilder.Password = Environment.GetEnvironmentVariable("DB_PASSWORD")
            ?? throw new InvalidOperationException("DB_PASSWORD is required when DB_AUTH_MODE is Password.");
        return NpgsqlDataSource.Create(connectionStringBuilder.ConnectionString);
    }

    TokenCredential credential = authMode.ToLowerInvariant() switch
    {
        "managedidentity" => CreateManagedIdentityCredential(),
        "defaultazurecredential" => new DefaultAzureCredential(),
        _ => throw new InvalidOperationException(
            "DB_AUTH_MODE must be Password, DefaultAzureCredential, or ManagedIdentity.")
    };

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString);
    dataSourceBuilder.UsePeriodicPasswordProvider(
        async (_, cancellationToken) =>
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]),
                cancellationToken);
            return token.Token;
        },
        TimeSpan.FromMinutes(50),
        TimeSpan.FromSeconds(5));
    return dataSourceBuilder.Build();
}

static ManagedIdentityCredential CreateManagedIdentityCredential()
{
    var clientId = Environment.GetEnvironmentVariable("DB_MANAGED_IDENTITY_CLIENT_ID");
    return new ManagedIdentityCredential(string.IsNullOrWhiteSpace(clientId)
        ? ManagedIdentityId.SystemAssigned
        : ManagedIdentityId.FromUserAssignedClientId(clientId));
}
