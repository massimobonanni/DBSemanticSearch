using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DBSemanticSearch.Contracts;

namespace DBSemanticSearch.Client;

public sealed class SemanticSearchClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<TextDto> AddAsync(string text, CancellationToken cancellationToken = default) =>
        PostAsync<AddTextRequest, TextDto>("api/texts", new(text), cancellationToken);

    public Task<BatchResponse> AddBatchAsync(TextBatchRequest batch, CancellationToken cancellationToken = default) =>
        PostAsync<TextBatchRequest, BatchResponse>("api/texts/batch", batch, cancellationToken);

    public async Task<BatchResponse> AddBatchJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync("api/texts/batch", content, cancellationToken);
        return await ReadAsync<BatchResponse>(response, cancellationToken);
    }

    public Task<SearchResponse> SearchAsync(string text, CancellationToken cancellationToken = default) =>
        PostAsync<SearchRequest, SearchResponse>("api/search", new(text), cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            ErrorResponse? error;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new HttpRequestException(
                    $"Errore HTTP {(int)response.StatusCode}: risposta del servizio non valida.",
                    ex, response.StatusCode);
            }
            throw new HttpRequestException(error?.Error ?? $"Errore HTTP {(int)response.StatusCode}.",
                null, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new JsonException("La risposta del servizio è vuota.");
    }
}
