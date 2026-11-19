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
        // A watermark is version 5. A barcode is version 6. Older files record that when they are saved.
        var required = RequiredSchema(definition);
        if (definition.SchemaVersion < required)
            definition.SchemaVersion = required;
        return JsonSerializer.Serialize(definition, Options);
    }

    private static int RequiredSchema(ReportDefinition definition)
    {
        if (ContainsBarcode(definition))
            return 6;
        if (definition.Page?.Watermark is not null)
            return 5;
        return definition.SchemaVersion;
    }

    private static bool ContainsBarcode(ReportDefinition definition) =>
        ContainsBarcode(definition.PageHeader?.Blocks)
        || ContainsBarcode(definition.Header?.Blocks)
        || ContainsBarcode(definition.Body?.Blocks)
        || ContainsBarcode(definition.Footer?.Blocks)
        || ContainsBarcode(definition.PageFooter?.Blocks);

    private static bool ContainsBarcode(List<Block>? blocks)
    {
        if (blocks is null)
            return false;

        foreach (var block in blocks)
        {
            switch (block)
            {
                case BarcodeBlock:
                    return true;
                case ColumnsBlock columns:
                    foreach (var column in columns.Columns)
                    {
                        if (ContainsBarcode(column.Blocks))
                            return true;
                    }
                    break;
                case PanelBlock panel when ContainsBarcode(panel.Blocks):
                    return true;
                case RepeatBlock repeat when ContainsBarcode(repeat.Blocks):
                    return true;
            }
        }

        return false;
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
