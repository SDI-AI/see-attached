using System.Text.RegularExpressions;

namespace ReportCreator.Model;

public static partial class StyleRules
{
    public static bool IsHexColor(string? color) =>
        color is not null && HexColor().IsMatch(color);

    public static bool HasAtMostTwoDecimalPlaces(decimal value) =>
        value * 100m == decimal.Truncate(value * 100m);

    public static bool IsBindingPath(string? path) =>
        path is not null && BindingPath().IsMatch(path);

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex BindingPath();
}
