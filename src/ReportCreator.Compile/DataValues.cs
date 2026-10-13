using System.Globalization;
using System.Text.Json;

namespace ReportCreator.Compile;

public static class DataValues
{
    public static bool TryResolve(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var part in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out var next))
            {
                value = default;
                return false;
            }

            value = next;
        }

        return value.ValueKind != JsonValueKind.Undefined;
    }

    public static string Format(JsonElement value, string? format, CultureInfo culture)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "";

        if (string.IsNullOrWhiteSpace(format))
            return Raw(value);

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return FormatNumber(number, format, culture) ?? Raw(value);

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";
            if (LooksLikeDateFormat(format)
                && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            {
                return FormatDate(date, format, culture) ?? text;
            }

            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return FormatNumber(parsed, format, culture) ?? text;

            return text;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => Raw(value)
        };
    }

    public static decimal? AsDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static string Raw(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();

    private static string? FormatNumber(decimal number, string format, CultureInfo culture)
    {
        try
        {
            return number.ToString(format, culture);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? FormatDate(DateTime date, string format, CultureInfo culture)
    {
        try
        {
            return date.ToString(format, culture);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool LooksLikeDateFormat(string format)
    {
        foreach (var character in format)
        {
            if (character is 'y' or 'M' or 'd')
                return true;
        }

        return false;
    }
}
