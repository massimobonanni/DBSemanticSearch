using System.Net;
using System.Text.Json;
using DBSemanticSearch.Contracts;
using DBSemanticSearch.Core.Entites;
using DBSemanticSearch.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DBSemanticSearch.Api;

public sealed class TextFunctions(TextService texts, ILogger<TextFunctions> logger)
{
    private const int MaxRequestBytes = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("AddText")]
    public async Task<HttpResponseData> AddText(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "texts")] HttpRequestData request)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        try
        {
            using var json = await ReadJsonAsync(request, cancellationToken);
            var text = ReadText(json.RootElement);
            var document = await texts.AddAsync(text, cancellationToken);
            return await RespondAsync(request, HttpStatusCode.Created, ToDto(document), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return await RespondAsync(request, HttpStatusCode.BadRequest, new ErrorResponse(ex.Message), cancellationToken);
        }
        catch (JsonException ex)
        {
            return await RespondAsync(request, HttpStatusCode.BadRequest, new ErrorResponse(ex.Message), cancellationToken);
        }
        catch (RequestTooLargeException)
        {
            return await RespondAsync(request, HttpStatusCode.RequestEntityTooLarge,
                new ErrorResponse("La richiesta supera 1 MiB."), cancellationToken);
        }
    }

    [Function("AddBatch")]
    public async Task<HttpResponseData> AddBatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "texts/batch")] HttpRequestData request)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        try
        {
            using var json = await ReadJsonAsync(request, cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("texts", out var entries))
                throw new ArgumentException("È richiesto un oggetto JSON con l'array 'texts'.");
            var batch = await BatchProcessor.ProcessAsync(entries, texts, logger, cancellationToken);
            return await RespondAsync(request, HttpStatusCode.OK, batch, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return await RespondAsync(request, HttpStatusCode.BadRequest, new ErrorResponse(ex.Message), cancellationToken);
        }
        catch (JsonException ex)
        {
            return await RespondAsync(request, HttpStatusCode.BadRequest, new ErrorResponse(ex.Message), cancellationToken);
        }
        catch (RequestTooLargeException)
        {
            return await RespondAsync(request, HttpStatusCode.RequestEntityTooLarge,
                new ErrorResponse("La richiesta supera 1 MiB."), cancellationToken);
        }
    }

    [Function("SearchTexts")]
    public async Task<HttpResponseData> Search(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "search")] HttpRequestData request)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        try
        {
            using var json = await ReadJsonAsync(request, cancellationToken);
            var hits = await texts.SearchAsync(ReadText(json.RootElement), cancellationToken);
            var result = new SearchResponse(hits.Select(hit => new SearchResult(ToDto(hit.Document), hit.Distance)).ToArray());
            return await RespondAsync(request, HttpStatusCode.OK, result, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return await RespondAsync(request, HttpStatusCode.BadRequest, new ErrorResponse(ex.Message), cancellationToken);
        }
        catch (JsonException ex)
        {
            return await RespondAsync(request, HttpStatusCode.BadRequest, new ErrorResponse(ex.Message), cancellationToken);
        }
        catch (RequestTooLargeException)
        {
            return await RespondAsync(request, HttpStatusCode.RequestEntityTooLarge,
                new ErrorResponse("La richiesta supera 1 MiB."), cancellationToken);
        }
    }

    private static string? ReadText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("text", out var value)
            || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            throw new ArgumentException("È richiesto un oggetto JSON con la proprietà stringa 'text'.");
        return value.GetString();
    }

    private static TextDto ToDto(TextDocument document) => new(document.Id, document.Text, document.CreatedAt);

    private static async Task<JsonDocument> ReadJsonAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await request.Body.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + count > MaxRequestBytes)
                throw new RequestTooLargeException();
            buffer.Write(chunk, 0, count);
        }

        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
    }

    private static async Task<HttpResponseData> RespondAsync<T>(
        HttpRequestData request, HttpStatusCode status, T body, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await JsonSerializer.SerializeAsync(response.Body, body, JsonOptions, cancellationToken);
        return response;
    }

    private sealed class RequestTooLargeException : Exception;
}
