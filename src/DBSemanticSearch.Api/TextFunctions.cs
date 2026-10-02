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
    private const string EmptyBodyMessage = "The request body is required.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new BatchTextsConverter() }
    };

    [Function("AddText")]
    public async Task<HttpResponseData> AddText(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "texts")] HttpRequestData request)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        try
        {
            var body = await ReadBodyAsync<AddTextRequest>(request, cancellationToken);
            var document = await texts.AddAsync(body.Text, cancellationToken);
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
                new ErrorResponse("The request exceeds 1 MiB."), cancellationToken);
        }
    }

    [Function("AddBatch")]
    public async Task<HttpResponseData> AddBatch(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "texts/batch")] HttpRequestData request)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        try
        {
            var body = await ReadBodyAsync<TextBatchRequest>(request, cancellationToken);
            var batch = await BatchProcessor.ProcessAsync(body.Texts, texts, logger, cancellationToken);
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
                new ErrorResponse("The request exceeds 1 MiB."), cancellationToken);
        }
    }

    [Function("SearchTexts")]
    public async Task<HttpResponseData> Search(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "search")] HttpRequestData request)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        try
        {
            var body = await ReadBodyAsync<SearchRequest>(request, cancellationToken);
            var hits = await texts.SearchAsync(body.Text, cancellationToken);
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
                new ErrorResponse("The request exceeds 1 MiB."), cancellationToken);
        }
    }

    [Function("DeleteAllTexts")]
    public async Task<HttpResponseData> DeleteAll(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "texts")] HttpRequestData request)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        try
        {
            await texts.DeleteAllAsync(cancellationToken);
            return request.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unable to delete all texts.");
            return await RespondAsync(request, HttpStatusCode.InternalServerError,
                new ErrorResponse("Unable to delete the texts. Please try again later."), cancellationToken);
        }
    }

    private static TextDto ToDto(TextDocument document) => new(document.Id, document.Text, document.CreatedAt);

    private static async Task<T> ReadBodyAsync<T>(HttpRequestData request, CancellationToken cancellationToken)
        where T : class
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

        if (buffer.Length == 0)
            throw new ArgumentException(EmptyBodyMessage);

        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(buffer, JsonOptions, cancellationToken)
            ?? throw new ArgumentException(EmptyBodyMessage);
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
