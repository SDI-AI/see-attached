using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReportCreator.Model;

public static class ReportJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Write(ReportDefinition definition) =>
        JsonSerializer.Serialize(definition, Options);

    public static ReportDefinition Read(string json) =>
        JsonSerializer.Deserialize<ReportDefinition>(json, Options)
        ?? throw new JsonException("The report file was empty.");

    public static JsonElement ReadData(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Report data must be a JSON object.");

        return document.RootElement.Clone();
    }
}
