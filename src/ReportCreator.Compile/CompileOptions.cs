using System.Globalization;
using System.Text.Json;

namespace ReportCreator.Compile;

public sealed class CompileOptions
{
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    public bool DesignMode { get; init; }

    public string? SelectedId { get; init; }

    // JSON object keyed by parameter name. A missing key uses the parameter default.
    public JsonElement Parameters { get; init; }

    // Wall-clock time for {print.date} and {print.time}. Null uses the local time of the compile.
    public DateTimeOffset? Clock { get; init; }
}
