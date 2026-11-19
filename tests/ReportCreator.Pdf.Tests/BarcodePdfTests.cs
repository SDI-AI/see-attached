using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReportCreator.Compile;
using ReportCreator.Model;
using ReportCreator.Pdf;

namespace ReportCreator.Pdf.Tests;

public partial class PdfExportTests
{
    [Fact]
    public async Task Export_PrintsTheBarcodeSvg()
    {
        var definition = new ReportDefinition
        {
            Name = "Shipping label",
            Page = new PageSetup
            {
                Margin = new Margin { Top = 12, Right = 12, Bottom = 12, Left = 12 }
            },
            Body =
            {
                Blocks =
                [
                    new TextBlock { Id = "title", Text = "Sample label" },
                    new BarcodeBlock
                    {
                        Id = "tracking",
                        Symbology = BarcodeSymbology.Code128,
                        Value = "TRK100000001",
                        ShowText = true,
                        Color = "#0B3D91",
                        WidthMm = 70,
                        HeightMm = 18
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new { });
        var html = ReportCompiler.Compile(definition, data, new CompileOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US")
        });
        var svgStart = html.IndexOf("<svg ", StringComparison.Ordinal);
        var svgEnd = html.IndexOf("</svg>", svgStart, StringComparison.Ordinal);
        Assert.True(svgStart > 0 && svgEnd > svgStart);
        var svg = html[svgStart..(svgEnd + "</svg>".Length)];
        Assert.Contains("<rect ", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#0B3D91\"", svg, StringComparison.Ordinal);
        Assert.Contains("width=\"70mm\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<?xml", svg, StringComparison.Ordinal);

        await using var exporter = await ReportPdfExporter.LaunchAsync(new PdfExportOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US")
        });
        var pdf = await exporter.ExportAsync(definition, data, TestContext.Current.CancellationToken);

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf[..5]), StringComparison.Ordinal);
        Assert.Contains("TRK100000001", ExtractText(pdf), StringComparison.Ordinal);
        Assert.Contains("Sample label", ExtractText(pdf), StringComparison.Ordinal);
        var streams = ContentStreams(pdf);
        var rectangles = Regex.Matches(streams, @"\bre\b").Count;
        Assert.True(rectangles > 20, "Expected the inline SVG bars to paint as PDF rectangles, got " + rectangles + ".");
    }
}
