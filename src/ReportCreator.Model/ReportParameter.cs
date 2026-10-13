using System.Globalization;

namespace ReportCreator.Model;

public enum ParameterKind
{
    String,
    Number,
    Date,
    Bool
}

public sealed class ReportParameter
{
    public string Name { get; set; } = "";

    public ParameterKind Kind { get; set; } = ParameterKind.String;

    public string? Default { get; set; }
}

public static class ParameterRules
{
    public static bool IsName(string? name) =>
        name is not null
        && name.Length is > 0 and <= 64
        && !name.Contains('.')
        && StyleRules.IsBindingPath(name);

    public static bool DefaultMatches(ReportParameter parameter, out string problem)
    {
        problem = "";
        if (string.IsNullOrWhiteSpace(parameter.Default))
            return true;

        switch (parameter.Kind)
        {
            case ParameterKind.String when parameter.Default.Length > 500:
                problem = "is longer than 500 characters";
                return false;
            case ParameterKind.String:
                return true;
            case ParameterKind.Number when decimal.TryParse(parameter.Default, NumberStyles.Number, CultureInfo.InvariantCulture, out _):
                return true;
            case ParameterKind.Number:
                problem = "is not a number";
                return false;
            case ParameterKind.Date when DateTime.TryParse(parameter.Default, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _):
                return true;
            case ParameterKind.Date:
                problem = "is not a date";
                return false;
            case ParameterKind.Bool when parameter.Default.Equals("true", StringComparison.OrdinalIgnoreCase)
                || parameter.Default.Equals("false", StringComparison.OrdinalIgnoreCase):
                return true;
            case ParameterKind.Bool:
                problem = "is not true or false";
                return false;
            default:
                problem = "does not match the parameter kind";
                return false;
        }
    }
}

public static class PrintPaths
{
    public const string ReportName = "report.name";

    public const string Date = "print.date";

    public const string Time = "print.time";

    public static bool IsPrintField(string? path) =>
        path is ReportName or Date or Time;

    // {param.name} is a parameter. A longer path such as param.customer.name stays data.
    public static bool TryParameterName(string? path, out string name)
    {
        name = "";
        const string prefix = "param.";
        if (path is null || !path.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        name = path[prefix.Length..];
        return ParameterRules.IsName(name);
    }
}
