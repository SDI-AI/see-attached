using System.Globalization;
using System.Text.Json;

namespace ReportCreator.Pdf;

public sealed class PdfExportOptions
{
    /// <summary>
    /// Launch Chromium with the flags a locked-down container needs:
    /// <c>--disable-dev-shm-usage</c> and <c>--no-sandbox</c>.
    /// Turn this on for Azure Container Apps. Leave it off on Windows and on a normal VM.
    /// </summary>
    public bool Container { get; init; }

    public CultureInfo? Culture { get; init; }

    // JSON object keyed by parameter name. A missing key uses the parameter default.
    public JsonElement Parameters { get; init; }

    // Wall-clock time for print date and print time. Null uses the local time of the export.
    public DateTimeOffset? Clock { get; init; }
}
