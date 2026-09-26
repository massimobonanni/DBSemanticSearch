using System.Text.Json;
using DBSemanticSearch.Contracts;
using DBSemanticSearch.Core.Services;
using Microsoft.Extensions.Logging;

namespace DBSemanticSearch.Api;

internal static class BatchProcessor
{
    internal static async Task<BatchResponse> ProcessAsync(
        JsonElement entries, TextService texts, ILogger logger, CancellationToken cancellationToken)
    {
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() is < 1 or > 100)
            throw new ArgumentException("L'array 'texts' deve contenere da 1 a 100 elementi.");

        var results = new List<BatchItemResult>();
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                results.Add(new(index++, null, "L'elemento deve essere una stringa."));
                continue;
            }

            string normalized;
            try
            {
                normalized = TextService.Validate(entry.GetString());
            }
            catch (ArgumentException ex)
            {
                results.Add(new(index, null, ex.Message));
                index++;
                continue;
            }

            try
            {
                var document = await texts.AddAsync(normalized, cancellationToken);
                results.Add(new(index, new TextDto(document.Id, document.Text, document.CreatedAt), null));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Elaborazione non riuscita per l'elemento {Index}", index);
                results.Add(new(index, null, "Impossibile elaborare il testo. Riprovare più tardi."));
            }

            index++;
        }

        return new(results.Count(result => result.Document is not null), results);
    }
}
