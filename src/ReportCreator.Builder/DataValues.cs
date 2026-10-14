using System.Globalization;
using System.Text.Json;
using ReportCreator.Model;

namespace ReportCreator.Builder;

internal static class DataValues
{
    public static string Text(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return "";
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => current.GetRawText()
        };
    }

    public static string Canonical(string text, DataFieldType type)
    {
        switch (type)
        {
            case DataFieldType.Number:
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var number)
                    && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                    throw new ArgumentException("Enter a number.");
                return number.ToString(CultureInfo.InvariantCulture);
            case DataFieldType.Bool:
                if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return "true";
                if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return "false";
                throw new ArgumentException("Enter true or false.");
            case DataFieldType.Date:
                var trimmed = text.Trim();
                if (trimmed.Length == 0)
                    return "";
                if (!DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new ArgumentException("Enter a date as yyyy-MM-dd.");
                return trimmed;
            case DataFieldType.Object:
            case DataFieldType.Array:
                return Container(text, type);
            default:
                return text;
        }
    }

    public static bool TryRow(JsonElement root, string arrayPath, int index, out JsonElement row)
    {
        row = root;
        foreach (var part in arrayPath.Split('.'))
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(part, out row))
                return false;
        }

        if (row.ValueKind != JsonValueKind.Array || index < 0 || index >= row.GetArrayLength())
            return false;
        row = row[index];
        return row.ValueKind == JsonValueKind.Object;
    }

    public static int RowCount(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return 0;
        }

        return current.ValueKind == JsonValueKind.Array ? current.GetArrayLength() : 0;
    }

    public static IEnumerable<(string Path, DataField Field)> Leaves(List<DataField>? fields, string prefix)
    {
        foreach (var field in fields ?? [])
        {
            var path = prefix.Length == 0 ? field.Name : prefix + "." + field.Name;
            if (field.Type == DataFieldType.Object)
            {
                foreach (var child in Leaves(field.Fields, path))
                    yield return child;
            }
            else
            {
                yield return (path, field);
            }
        }
    }

    public static bool UnderArray(DataDocument document, string path)
    {
        var parts = path.Split('.');
        return parts.Length > 1 && InsideArray(document, string.Join('.', parts[..^1]));
    }

    public static bool InsideArray(DataDocument document, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var parts = path.Split('.');
        for (var length = 1; length <= parts.Length; length++)
        {
            if (DataEdit.Find(document, string.Join('.', parts[..length])) is { Type: DataFieldType.Array })
                return true;
        }

        return false;
    }

    public static string TypeLabel(DataFieldType type) => type switch
    {
        DataFieldType.Number => "number",
        DataFieldType.Date => "date",
        DataFieldType.Bool => "bool",
        DataFieldType.Object => "object",
        DataFieldType.Array => "array",
        _ => "string"
    };

    public static DataFieldType ParseType(string? label) => label switch
    {
        "number" => DataFieldType.Number,
        "date" => DataFieldType.Date,
        "bool" => DataFieldType.Bool,
        "object" => DataFieldType.Object,
        "array" => DataFieldType.Array,
        _ => DataFieldType.String
    };

    public static string Short(string text, int max)
    {
        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= max ? flat : flat[..(max - 1)] + "…";
    }

    private static string Container(string text, DataFieldType type)
    {
        var trimmed = text.Trim();
        var expect = type == DataFieldType.Object ? JsonValueKind.Object : JsonValueKind.Array;
        var message = type == DataFieldType.Object ? "Enter a JSON object." : "Enter a JSON array.";
        if (trimmed.Length == 0)
            return expect == JsonValueKind.Object ? "{}" : "[]";
        try
        {
            using var parsed = JsonDocument.Parse(trimmed);
            if (parsed.RootElement.ValueKind != expect)
                throw new ArgumentException(message);
        }
        catch (JsonException)
        {
            throw new ArgumentException(message);
        }

        return trimmed;
    }
}
