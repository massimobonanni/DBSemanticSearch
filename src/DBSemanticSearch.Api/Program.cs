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
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var host = Environment.GetEnvironmentVariable("DB_HOST");
    var user = Environment.GetEnvironmentVariable("DB_USER");
    var password = Environment.GetEnvironmentVariable("DB_PASSWORD");
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user)
        || string.IsNullOrEmpty(password))
        throw new InvalidOperationException("Configurare DB_CONNECTION_STRING o DB_HOST, DB_USER e DB_PASSWORD.");

    connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = host,
        Database = Environment.GetEnvironmentVariable("DB_NAME") ?? "semantic_search",
        Username = user,
        Password = password,
        SslMode = SslMode.VerifyFull
    }.ConnectionString;
}

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<ITextRepository>(serviceProvider =>
    new PostgresTextRepository(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(),
        settings.Dimensions));
builder.Services.AddSingleton<IEmbeddingService, FoundryEmbeddingService>();
builder.Services.AddSingleton<TextService>();
builder.Build().Run();
