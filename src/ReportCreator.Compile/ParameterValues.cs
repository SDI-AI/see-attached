using System.Globalization;
using System.Text.Json;
using ReportCreator.Model;

namespace ReportCreator.Compile;

static class ParameterValues
{
    public static bool TryGet(ReportDefinition definition, JsonElement supplied, string name, out JsonElement value)
    {
        value = default;
        var parameter = definition.Parameters?.FirstOrDefault(item => item.Name == name);
        if (parameter is null || !Enum.IsDefined(parameter.Kind))
            return false;

        if (supplied.ValueKind == JsonValueKind.Object
            && supplied.TryGetProperty(name, out var given)
            && given.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            return TryCoerce(parameter.Kind, given, out value);

        if (string.IsNullOrWhiteSpace(parameter.Default))
            return false;

        return TryCoerce(parameter.Kind, JsonSerializer.SerializeToElement(parameter.Default), out value);
    }

    private static bool TryCoerce(ParameterKind kind, JsonElement given, out JsonElement value)
    {
        value = default;
        switch (kind)
        {
            case ParameterKind.String:
                if (given.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined)
                    return false;
                var text = given.ValueKind == JsonValueKind.String ? given.GetString() ?? "" : given.ToString();
                value = JsonSerializer.SerializeToElement(text);
                return true;
            case ParameterKind.Number:
                if (DataValues.AsDecimal(given) is not decimal number)
                    return false;
                value = JsonSerializer.SerializeToElement(number);
                return true;
            case ParameterKind.Date:
                if (given.ValueKind != JsonValueKind.String
                    || !DateTime.TryParse(given.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                    return false;
                var written = date.TimeOfDay == TimeSpan.Zero
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
                value = JsonSerializer.SerializeToElement(written);
                return true;
            case ParameterKind.Bool:
                if (given.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    value = JsonSerializer.SerializeToElement(given.ValueKind == JsonValueKind.True);
                    return true;
                }

                if (given.ValueKind == JsonValueKind.String)
                {
                    var flag = given.GetString();
                    if (flag is not null && (flag.Equals("true", StringComparison.OrdinalIgnoreCase) || flag.Equals("false", StringComparison.OrdinalIgnoreCase)))
                    {
                        value = JsonSerializer.SerializeToElement(flag.Equals("true", StringComparison.OrdinalIgnoreCase));
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }
}
