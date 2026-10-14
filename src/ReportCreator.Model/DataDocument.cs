using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReportCreator.Model;

public enum DataFieldType
{
    String,
    Number,
    Date,
    Bool,
    Object,
    Array
}

public sealed class DataField
{
    public string Name { get; set; } = "";

    public DataFieldType Type { get; set; } = DataFieldType.String;

    public List<DataField> Fields { get; set; } = [];
}

public sealed class DataDocument
{
    public const int CurrentSchemaVersion = 1;

    public string Kind { get; set; } = "data";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = "Data";

    public List<DataField> Fields { get; set; } = [];

    public JsonElement Values { get; set; }
}

public static class DataJson
{
    public static string Write(DataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Kind = "data";
        document.Fields ??= [];
        if (document.Values.ValueKind != JsonValueKind.Object)
            document.Values = JsonSerializer.SerializeToElement(new JsonObject());
        return JsonSerializer.Serialize(document, ReportJson.Options);
    }

    public static DataDocument Read(string json)
    {
        var document = JsonSerializer.Deserialize<DataDocument>(json, ReportJson.Options)
            ?? throw new JsonException("The data file was empty.");
        document.Kind = "data";
        document.Fields ??= [];
        if (document.Values.ValueKind != JsonValueKind.Object)
            document.Values = JsonSerializer.SerializeToElement(new JsonObject());
        return document;
    }

    public static bool IsDocument(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var property in parsed.RootElement.EnumerateObject())
        {
            if (property.NameEquals("kind") || property.NameEquals("Kind"))
                return property.Value.ValueKind == JsonValueKind.String
                    && string.Equals(property.Value.GetString(), "data", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static DataDocument ReadAny(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Report data must be a JSON object.");
        if (IsReport(root))
            throw new JsonException("This file is a report layout. Open it from File → Open. A data file is .rcdata, or a JSON object of values.");
        if (IsDocument(json))
            return Read(json);
        return FromValues(root.Clone());
    }

    public static JsonElement ReadValues(string json) => ReadAny(json).Values.Clone();

    public static DataDocument FromValues(JsonElement values, string name = "Data")
    {
        if (values.ValueKind != JsonValueKind.Object)
            throw new JsonException("Report data must be a JSON object.");

        return new DataDocument
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Data" : name.Trim(),
            Values = values.Clone(),
            Fields = InferFields(values)
        };
    }

    public static DataDocument Adopt(DataDocument document, JsonElement values)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (values.ValueKind != JsonValueKind.Object)
            throw new JsonException("Report data must be a JSON object.");

        return new DataDocument
        {
            Name = string.IsNullOrWhiteSpace(document.Name) ? "Data" : document.Name,
            SchemaVersion = document.SchemaVersion == 0 ? DataDocument.CurrentSchemaVersion : document.SchemaVersion,
            Values = values.Clone(),
            Fields = AdoptFields(document.Fields, values)
        };
    }

    public static DataDocument Clone(DataDocument document) => Read(Write(document));

    private static bool IsReport(JsonElement root)
    {
        var page = false;
        var schema = false;
        var kind = false;
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("page") || property.NameEquals("Page"))
                page = true;
            if (property.NameEquals("schemaVersion") || property.NameEquals("SchemaVersion"))
                schema = true;
            if (property.NameEquals("kind") || property.NameEquals("Kind"))
                kind = true;
        }

        return page && schema && !kind;
    }

    private static List<DataField> InferFields(JsonElement values)
    {
        var fields = new List<DataField>();
        if (values.ValueKind != JsonValueKind.Object)
            return fields;
        foreach (var property in values.EnumerateObject())
            fields.Add(InferField(property.Name, property.Value));
        return fields;
    }

    internal static DataField InferField(string name, JsonElement value)
    {
        var field = new DataField { Name = name };
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                field.Type = DataFieldType.Number;
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                field.Type = DataFieldType.Bool;
                break;
            case JsonValueKind.Object:
                field.Type = DataFieldType.Object;
                field.Fields = InferFields(value);
                break;
            case JsonValueKind.Array:
                field.Type = DataFieldType.Array;
                field.Fields = InferColumns(value);
                break;
            case JsonValueKind.String when IsDate(value.GetString()):
                field.Type = DataFieldType.Date;
                break;
            default:
                field.Type = DataFieldType.String;
                break;
        }

        return field;
    }

    private static List<DataField> InferColumns(JsonElement array)
    {
        var fields = new List<DataField>();
        foreach (var row in array.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var property in row.EnumerateObject())
            {
                if (fields.All(field => field.Name != property.Name))
                    fields.Add(InferField(property.Name, property.Value));
            }
        }

        return fields;
    }

    private static List<DataField> AdoptFields(List<DataField>? existing, JsonElement values)
    {
        existing ??= [];
        if (values.ValueKind != JsonValueKind.Object)
            return [];

        var fields = new List<DataField>();
        foreach (var property in values.EnumerateObject())
        {
            var prior = existing.FirstOrDefault(field => field.Name == property.Name);
            fields.Add(prior is not null && Compatible(prior, property.Value)
                ? Keep(prior, property.Value)
                : InferField(property.Name, property.Value));
        }

        return fields;
    }

    private static DataField Keep(DataField prior, JsonElement value)
    {
        var field = new DataField { Name = prior.Name, Type = prior.Type };
        if (prior.Type == DataFieldType.Object)
            field.Fields = AdoptFields(prior.Fields, value);
        else if (prior.Type == DataFieldType.Array)
            field.Fields = AdoptColumns(prior.Fields, value);
        return field;
    }

    private static List<DataField> AdoptColumns(List<DataField>? existing, JsonElement array)
    {
        existing ??= [];
        var seen = new List<(string Name, JsonElement Value)>();
        var order = new List<string>();
        foreach (var row in array.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var property in row.EnumerateObject())
            {
                if (seen.Any(item => item.Name == property.Name))
                    continue;
                seen.Add((property.Name, property.Value));
                order.Add(property.Name);
            }
        }

        if (order.Count == 0)
            return existing.Select(CloneField).ToList();

        var fields = new List<DataField>();
        foreach (var prior in existing)
        {
            var sample = seen.FirstOrDefault(item => item.Name == prior.Name);
            if (sample.Name is null)
                continue;
            fields.Add(Compatible(prior, sample.Value) ? Keep(prior, sample.Value) : InferField(prior.Name, sample.Value));
        }

        foreach (var name in order)
        {
            if (fields.Any(field => field.Name == name))
                continue;
            fields.Add(InferField(name, seen.First(item => item.Name == name).Value));
        }

        return fields;
    }

    private static DataField CloneField(DataField field) => new()
    {
        Name = field.Name,
        Type = field.Type,
        Fields = field.Fields?.Select(CloneField).ToList() ?? []
    };

    private static bool Compatible(DataField field, JsonElement value) => field.Type switch
    {
        DataFieldType.String => value.ValueKind == JsonValueKind.String,
        DataFieldType.Number => value.ValueKind == JsonValueKind.Number,
        DataFieldType.Bool => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        DataFieldType.Date => value.ValueKind == JsonValueKind.String && (string.IsNullOrEmpty(value.GetString()) || IsDate(value.GetString())),
        DataFieldType.Object => value.ValueKind == JsonValueKind.Object,
        DataFieldType.Array => value.ValueKind == JsonValueKind.Array,
        _ => false
    };

    internal static bool IsDate(string? text) =>
        !string.IsNullOrEmpty(text)
        && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

public static class DataValidator
{
    public static IReadOnlyList<string> Validate(DataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        if (!string.Equals(document.Kind, "data", StringComparison.OrdinalIgnoreCase))
            errors.Add("A data file must have kind \"data\".");
        if (document.SchemaVersion != DataDocument.CurrentSchemaVersion)
            errors.Add($"Data schema version {document.SchemaVersion} is not supported. Use version {DataDocument.CurrentSchemaVersion}.");
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Trim().Length > 120)
            errors.Add("The data name is required and must be 120 characters or fewer.");
        if (document.Values.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Object)
            errors.Add("Data values must be a JSON object.");

        document.Fields ??= [];
        CheckFields(document.Fields, document.Values, "", 0, errors);
        return errors;
    }

    private static void CheckFields(List<DataField> fields, JsonElement values, string scope, int depth, List<string> errors)
    {
        if (depth > 8)
        {
            errors.Add("Data fields can be nested at most 8 levels.");
            return;
        }

        if (fields.Count > 200)
            errors.Add($"{Where(scope)} has more than 200 fields.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var path = scope.Length == 0 ? field.Name : scope + "." + field.Name;
            if (!ParameterRules.IsName(field.Name))
                errors.Add($"Field '{path}' needs a name made of letters, digits, and underscores.");
            else if (!names.Add(field.Name))
                errors.Add($"Field '{path}' is declared more than once.");

            if (!Enum.IsDefined(field.Type))
            {
                errors.Add($"Field '{path}' has a type that is not supported.");
                continue;
            }

            field.Fields ??= [];
            JsonElement value = default;
            var present = values.ValueKind == JsonValueKind.Object && values.TryGetProperty(field.Name, out value);

            switch (field.Type)
            {
                case DataFieldType.String:
                    RejectChildren(field, path, errors);
                    if (present && value.ValueKind != JsonValueKind.String)
                        errors.Add($"Field '{path}' must be a string.");
                    break;
                case DataFieldType.Number:
                    RejectChildren(field, path, errors);
                    if (present && value.ValueKind != JsonValueKind.Number)
                        errors.Add($"Field '{path}' must be a number.");
                    break;
                case DataFieldType.Bool:
                    RejectChildren(field, path, errors);
                    if (present && value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                        errors.Add($"Field '{path}' must be true or false.");
                    break;
                case DataFieldType.Date:
                    RejectChildren(field, path, errors);
                    if (present && (value.ValueKind != JsonValueKind.String || !EmptyOrDate(value.GetString())))
                        errors.Add($"Field '{path}' must be a date in the form yyyy-MM-dd.");
                    break;
                case DataFieldType.Object:
                    if (present && value.ValueKind != JsonValueKind.Object)
                        errors.Add($"Field '{path}' must be an object.");
                    CheckFields(field.Fields, present ? value : default, path, depth + 1, errors);
                    break;
                case DataFieldType.Array:
                    if (present && value.ValueKind != JsonValueKind.Array)
                    {
                        errors.Add($"Field '{path}' must be an array.");
                        break;
                    }

                    if (!present)
                        break;
                    var index = 0;
                    foreach (var row in value.EnumerateArray())
                    {
                        if (index >= 1000)
                        {
                            errors.Add($"Field '{path}' has more than 1000 rows.");
                            break;
                        }

                        if (row.ValueKind != JsonValueKind.Object)
                            errors.Add($"Field '{path}' row {index + 1} must be an object.");
                        else
                            CheckFields(field.Fields, row, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", depth + 1, errors);
                        index++;
                    }

                    break;
            }
        }
    }

    private static void RejectChildren(DataField field, string path, List<string> errors)
    {
        if (field.Fields is { Count: > 0 })
            errors.Add($"Field '{path}' is a {field.Type.ToString().ToLowerInvariant()} and cannot contain other fields.");
    }

    private static bool EmptyOrDate(string? text) =>
        string.IsNullOrEmpty(text) || DataJson.IsDate(text);

    private static string Where(string scope) => scope.Length == 0 ? "The data file" : $"Field '{scope}'";
}
