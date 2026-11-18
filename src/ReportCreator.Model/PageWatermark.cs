using System.Text.Json.Serialization;

namespace ReportCreator.Model;

public enum WatermarkKind
{
    Text,
    Image
}

public enum WatermarkHorizontal
{
    Center,
    Left,
    Right
}

public enum WatermarkVertical
{
    Center,
    Top,
    Bottom
}

public sealed class PageWatermark
{
    public const decimal MaxFontSizePt = 120;

    public bool Enabled { get; set; } = true;

    public WatermarkKind Kind { get; set; } = WatermarkKind.Text;

    public string Text { get; set; } = "";

    public string? FontFamily { get; set; }

    public decimal FontSizePt { get; set; } = 72;

    public string? Color { get; set; }

    public decimal? Opacity { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal RotationDegrees { get; set; }

    public WatermarkHorizontal Horizontal { get; set; } = WatermarkHorizontal.Center;

    public WatermarkVertical Vertical { get; set; } = WatermarkVertical.Center;

    public string? Source { get; set; }

    public decimal? WidthMm { get; set; }

    public decimal? HeightMm { get; set; }
}
