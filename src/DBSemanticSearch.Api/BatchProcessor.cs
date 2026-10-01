using DBSemanticSearch.Contracts;
using DBSemanticSearch.Core.Services;
using Microsoft.Extensions.Logging;

namespace DBSemanticSearch.Api;

internal static class BatchProcessor
{
    internal static async Task<BatchResponse> ProcessAsync(
        IReadOnlyList<string?>? entries, TextService texts, ILogger logger, CancellationToken cancellationToken)
    {
        if (entries is null || entries.Count is < 1 or > 100)
            throw new ArgumentException("The 'texts' array must contain between 1 and 100 items.");

        var nonStringIndexes = (entries as BatchTexts)?.NonStringIndexes;
        var results = new List<BatchItemResult>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            if (nonStringIndexes?.Contains(index) == true)
            {
                results.Add(new(index, null, "The item must be a string."));
                continue;
            }

            string normalized;
            try
            {
                normalized = TextService.Validate(entries[index]);
            }
            catch (ArgumentException ex)
            {
                results.Add(new(index, null, ex.Message));
                continue;
            }

            try
            {
                var document = await texts.AddAsync(normalized, cancellationToken);
                results.Add(new(index, new TextDto(document.Id, document.Text, document.CreatedAt), null));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to process item {Index}", index);
                results.Add(new(index, null, "Unable to process the text. Please try again later."));
            }
        }

        return new(results.Count(result => result.Document is not null), results);
    }
}
