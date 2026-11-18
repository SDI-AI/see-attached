using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ReportCreator.Model;

public static class ReportJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { OmitEmptyParameters }
        };
        return options;
    }

    private static void OmitEmptyParameters(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(ReportDefinition))
            return;

        foreach (var property in typeInfo.Properties)
        {
            if (property.Name is not ("parameters" or "Parameters"))
                continue;

            property.ShouldSerialize = static (parent, _) =>
                parent is ReportDefinition definition && definition.Parameters is { Count: > 0 };
        }
    }

    public static string Write(ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // A watermark is a version 5 feature. Older files that gain one record that when they are saved.
        if (definition.Page?.Watermark is not null && definition.SchemaVersion < ReportDefinition.CurrentSchemaVersion)
            definition.SchemaVersion = ReportDefinition.CurrentSchemaVersion;
        return JsonSerializer.Serialize(definition, Options);
    }

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
