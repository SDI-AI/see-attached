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

    public static bool IsFontFamily(string? family) =>
        family is not null && AllowedFonts.Contains(family);

    public static string FontStack(string? family) => family switch
    {
        "Arial" => "Arial, \"Source Sans 3\", sans-serif",
        "Segoe UI" => "\"Segoe UI\", \"Source Sans 3\", sans-serif",
        "Times New Roman" => "\"Times New Roman\", \"Source Sans 3\", serif",
        "Georgia" => "Georgia, \"Source Sans 3\", serif",
        "Courier New" => "\"Courier New\", \"Source Sans 3\", monospace",
        _ => "\"Source Sans 3\", sans-serif"
    };

    private static readonly HashSet<string> AllowedFonts = new(StringComparer.Ordinal)
    {
        "Source Sans 3",
        "Arial",
        "Segoe UI",
        "Times New Roman",
        "Georgia",
        "Courier New"
    };

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex BindingPath();
}
