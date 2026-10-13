using System.Globalization;

namespace ReportCreator.Compile;

public sealed class CompileOptions
{
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    public bool DesignMode { get; init; }

    public string? SelectedId { get; init; }
}
