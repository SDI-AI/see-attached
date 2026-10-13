using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using ReportCreator.Compile;
using ReportCreator.Model;

namespace ReportCreator.Pdf;

public sealed class ReportPdfExporter : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly PdfExportOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ReportPdfExporter(IPlaywright playwright, IBrowser browser, PdfExportOptions options)
    {
        _playwright = playwright;
        _browser = browser;
        _options = options;
    }

    public static async Task<ReportPdfExporter> LaunchAsync(PdfExportOptions? options = null)
    {
        options ??= new PdfExportOptions();
        var playwright = await Playwright.CreateAsync();
        try
        {
            string[] args = options.Container
                ? ["--disable-dev-shm-usage", "--no-sandbox"]
                : [];
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = args
            });
            return new ReportPdfExporter(playwright, browser, options);
        }
        catch (Exception exception)
        {
            playwright.Dispose();
            throw new InvalidOperationException(
                "Playwright could not launch Chromium. Install it once with the playwright.ps1 script in this project's output folder: .\\playwright.ps1 install chromium",
                exception);
        }
    }

    public async Task<byte[]> ExportAsync(
        ReportDefinition definition,
        JsonElement data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var html = ReportCompiler.Compile(definition, data, new CompileOptions
            {
                Culture = _options.Culture ?? CultureInfo.CurrentCulture,
                Parameters = _options.Parameters,
                Clock = _options.Clock
            });
            var page = await _browser.NewPageAsync();
            try
            {
                await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Print });
                await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });
                await page.EvaluateAsync("() => document.fonts.ready");
                return await page.PdfAsync(new PagePdfOptions
                {
                    PreferCSSPageSize = true,
                    PrintBackground = true
                });
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
        _gate.Dispose();
    }
}
