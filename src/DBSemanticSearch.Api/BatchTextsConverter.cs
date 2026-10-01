using System.Text.Json;
using System.Text.Json.Serialization;

namespace DBSemanticSearch.Api;

internal sealed class BatchTexts : List<string?>
{
    public HashSet<int> NonStringIndexes { get; } = [];
}

// Keeps non-string items so the batch can report a per-item error instead of failing the whole request.
internal sealed class BatchTextsConverter : JsonConverter<IReadOnlyList<string?>>
{
    public override IReadOnlyList<string?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("The 'texts' property must be an array.");

        var texts = new BatchTexts();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    texts.Add(reader.GetString());
                    break;
                case JsonTokenType.Null:
                    texts.Add(null);
                    break;
                default:
                    texts.NonStringIndexes.Add(texts.Count);
                    texts.Add(null);
                    reader.Skip();
                    break;
            }
        }

        return texts;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string?> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var text in value)
            writer.WriteStringValue(text);
        writer.WriteEndArray();
    }
}
