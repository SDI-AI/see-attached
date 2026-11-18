using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReportCreator.Compile;
using ReportCreator.Model;

namespace ReportCreator.Compile.Tests;

public class WatermarkTests
{
    private const string TinyPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public void Watermark_RoundTripsThroughJsonAndAPackage()
    {
        var definition = DraftReport();
        definition.SchemaVersion = 4;
        var json = ReportJson.Write(definition);
        var again = ReportJson.Read(json);

        Assert.Equal(5, definition.SchemaVersion);
        Assert.Equal(5, again.SchemaVersion);
        Assert.Equal("DRAFT", again.Page.Watermark!.Text);
        Assert.Equal(120, again.Page.Watermark.FontSizePt);
        Assert.Equal("Segoe UI", again.Page.Watermark.FontFamily);
        Assert.Equal("#C0C0C0", again.Page.Watermark.Color);
        Assert.Equal(WatermarkHorizontal.Center, again.Page.Watermark.Horizontal);
        Assert.Equal(WatermarkVertical.Center, again.Page.Watermark.Vertical);
        Assert.True(again.Page.Watermark.Enabled);
        Assert.Equal(0, again.Page.Watermark.RotationDegrees);

        var path = Path.Combine(Path.GetTempPath(), "watermark-" + Guid.NewGuid().ToString("N") + ".rcreport");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("definition.json").Open()))
                writer.Write(json);
            using var read = ZipFile.OpenRead(path);
            var entry = read.GetEntry("definition.json");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry.Open());
            var packed = ReportJson.Read(reader.ReadToEnd());
            Assert.Equal("DRAFT", packed.Page.Watermark!.Text);
            Assert.Equal(120, packed.Page.Watermark.FontSizePt);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Watermark_IsOptionalAndLeavesOlderReportsAlone()
    {
        var invoice = ReportJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "invoice", "definition.json")));
        Assert.Equal(1, invoice.SchemaVersion);
        Assert.Null(invoice.Page.Watermark);
        Assert.Empty(ReportValidator.Validate(invoice));
        var written = ReportJson.Write(invoice);
        Assert.Equal(1, invoice.SchemaVersion);
        Assert.Contains("\"schemaVersion\": 1", written, StringComparison.Ordinal);
        Assert.DoesNotContain("watermark", written, StringComparison.OrdinalIgnoreCase);

        var html = ReportCompiler.Compile(invoice, ReportJson.ReadData(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "invoice", "data.json"))));
        Assert.DoesNotContain("class=\"page-watermark\"", html, StringComparison.Ordinal);

        var orders = ReportJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "orders", "definition.json")));
        Assert.Equal(4, orders.SchemaVersion);
        Assert.Empty(ReportValidator.Validate(orders));
    }

    [Fact]
    public void Watermark_Allows120PointsAndKeepsTheBodyCap()
    {
        var body = new ReportDefinition
        {
            Body = { Blocks = [new TextBlock { Id = "title", Text = "Hello", FontSizePt = 120 }] }
        };
        Assert.Contains(ReportValidator.Validate(body), error => error.Contains("6 and 96", StringComparison.Ordinal));

        var mark = DraftReport();
        Assert.Empty(ReportValidator.Validate(mark));
        mark.Page.Watermark!.FontSizePt = 121;
        Assert.Contains(ReportValidator.Validate(mark), error => error.Contains("6 and 120", StringComparison.Ordinal));
        mark.Page.Watermark.FontSizePt = 120;
        mark.Page.Watermark.Opacity = 1.5m;
        Assert.Contains(ReportValidator.Validate(mark), error => error.Contains("opacity", StringComparison.OrdinalIgnoreCase));
        mark.Page.Watermark.Opacity = 0.35m;
        mark.Page.Watermark.RotationDegrees = 181;
        Assert.Contains(ReportValidator.Validate(mark), error => error.Contains("rotation", StringComparison.OrdinalIgnoreCase));
        mark.Page.Watermark.RotationDegrees = -32.5m;
        mark.Page.Watermark.Enabled = false;
        mark.Page.Watermark.Text = "";
        Assert.Empty(ReportValidator.Validate(mark));
    }

    [Fact]
    public void Watermark_PrintsBehindTheBodyAndTakesNoFlow()
    {
        var marked = DraftReport();
        marked.Page.Watermark!.Opacity = 0.4m;
        marked.Page.Watermark.RotationDegrees = -32.5m;
        marked.Body.Blocks.Add(new TextBlock { Id = "body", Text = "Body line <safe>" });
        var plain = ReportJson.Read(ReportJson.Write(marked));
        plain.Page.Watermark = null;
        var data = JsonSerializer.SerializeToElement(new { });
        var html = ReportCompiler.Compile(marked, data);
        var without = ReportCompiler.Compile(plain, data);

        var markAt = html.IndexOf("<div class=\"page-watermark\"", StringComparison.Ordinal);
        var articleAt = html.IndexOf("<article", StringComparison.Ordinal);
        Assert.True(markAt >= 0 && markAt < articleAt);
        Assert.Equal(1, Count(html, "class=\"page-watermark\""));
        Assert.Contains("justify-content:center;align-items:center;", html, StringComparison.Ordinal);
        Assert.Contains("font-size:120pt;", html, StringComparison.Ordinal);
        Assert.Contains("color:#C0C0C0;", html, StringComparison.Ordinal);
        Assert.Contains("opacity:0.4;", html, StringComparison.Ordinal);
        Assert.Contains("transform:rotate(-32.5deg);", html, StringComparison.Ordinal);
        Assert.Contains("position: fixed;", html, StringComparison.Ordinal);
        Assert.Contains(">DRAFT</div>", html, StringComparison.Ordinal);
        Assert.Contains("Body line &lt;safe&gt;", html, StringComparison.Ordinal);
        var tag = Regex.Match(html, "<div class=\"page-watermark-text\"[^>]*>").Value;
        var style = WebUtility.HtmlDecode(Regex.Match(tag, "style=\"([^\"]*)\"").Groups[1].Value);
        Assert.Contains("font-family:\"Segoe UI\", \"Source Sans 3\", sans-serif;", style, StringComparison.Ordinal);
        Assert.Contains("font-size:120pt;", style, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family:\"Segoe UI\"", tag, StringComparison.Ordinal);

        var stripped = html.Remove(markAt, articleAt - markAt);
        Assert.Equal(without, stripped);

        marked.Page.Watermark.Enabled = false;
        var disabled = ReportCompiler.Compile(marked, data);
        Assert.Equal(without, disabled);
        Assert.DoesNotContain("DRAFT", disabled, StringComparison.Ordinal);
    }

    [Fact]
    public void Watermark_PlacesAnImageAndHonorsAlignment()
    {
        var definition = DraftReport();
        definition.Page.Watermark = new PageWatermark
        {
            Kind = WatermarkKind.Image,
            Source = TinyPng,
            Horizontal = WatermarkHorizontal.Right,
            Vertical = WatermarkVertical.Bottom,
            WidthMm = 28,
            HeightMm = 12,
            Opacity = 0.35m,
            RotationDegrees = 15
        };
        var html = ReportCompiler.Compile(definition, JsonSerializer.SerializeToElement(new { }));
        Assert.Contains("justify-content:flex-end;align-items:flex-end;", html, StringComparison.Ordinal);
        Assert.Contains("width:28mm;height:12mm;", html, StringComparison.Ordinal);
        Assert.Contains("opacity:0.35;transform:rotate(15deg);", html, StringComparison.Ordinal);
        Assert.Contains(TinyPng, html, StringComparison.Ordinal);
        Assert.DoesNotContain(">DRAFT<", html, StringComparison.Ordinal);

        definition.Page.Watermark.Kind = WatermarkKind.Text;
        definition.Page.Watermark.Text = "DRAFT";
        definition.Page.Watermark.Horizontal = WatermarkHorizontal.Left;
        definition.Page.Watermark.Vertical = WatermarkVertical.Top;
        var corner = ReportCompiler.Compile(definition, JsonSerializer.SerializeToElement(new { }));
        Assert.Contains("justify-content:flex-start;align-items:flex-start;", corner, StringComparison.Ordinal);
    }

    [Fact]
    public void Sample_OpensAndPrintsTheWatermark()
    {
        var definition = ReportJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "watermark", "definition.json")));
        var data = ReportJson.ReadData(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "watermark", "data.json")));
        Assert.Equal(5, definition.SchemaVersion);
        Assert.Empty(ReportValidator.Validate(definition));
        var again = ReportJson.Read(ReportJson.Write(definition));
        Assert.Equal("DRAFT", again.Page.Watermark!.Text);
        Assert.Equal(120, again.Page.Watermark.FontSizePt);
        var html = ReportCompiler.Compile(again, data);
        Assert.Contains("class=\"page-watermark\"", html, StringComparison.Ordinal);
        Assert.Contains(">DRAFT</div>", html, StringComparison.Ordinal);
        Assert.Contains("First sample line", html, StringComparison.Ordinal);
        Assert.Contains("End of sample", html, StringComparison.Ordinal);
        Assert.Contains("break-before", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, ">DRAFT</div>"));
    }

    private static int Count(string text, string token) => text.Split(token).Length - 1;

    private static ReportDefinition DraftReport() => new()
    {
        Name = "Sample notice",
        Page = new PageSetup
        {
            PageNumbers = PageNumberPlacement.BottomCenter,
            Watermark = new PageWatermark
            {
                Text = "DRAFT",
                FontFamily = "Segoe UI",
                FontSizePt = 120,
                Color = "#C0C0C0"
            }
        },
        Body = { Blocks = [new TextBlock { Id = "title", Text = "Sample notice" }] }
    };
}
