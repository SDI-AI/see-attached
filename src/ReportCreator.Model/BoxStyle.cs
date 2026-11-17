using System.Text.Json.Serialization;

namespace ReportCreator.Model;

public sealed class Spacing
{
    public decimal Top { get; set; }

    public decimal Right { get; set; }

    public decimal Bottom { get; set; }

    public decimal Left { get; set; }
}

public sealed class BorderLine
{
    public decimal WidthMm { get; set; }

    public string? Color { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Top { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Right { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Bottom { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Left { get; set; }

    public bool HasChosenSide => Top || Right || Bottom || Left;
}

public sealed class BoxStyle
{
    public string? FontFamily { get; set; }

    public decimal? FontSizePt { get; set; }

    public bool? Bold { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Italic { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Underline { get; set; }

    public string? Color { get; set; }

    public decimal? LineHeight { get; set; }

    public Spacing? Margin { get; set; }

    public Spacing? Padding { get; set; }

    public BorderLine? Border { get; set; }

    public string? Background { get; set; }
}

public sealed class TableStyle
{
    public string? FontFamily { get; set; }

    public decimal? FontSizePt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Italic { get; set; }

    public string? Color { get; set; }

    public decimal? CellPaddingMm { get; set; }

    public decimal? BorderWidthMm { get; set; }

    public string? BorderColor { get; set; }

    public string? HeaderBackground { get; set; }

    public string? Background { get; set; }
}
