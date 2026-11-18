using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReportCreator.Model;
using ReportCreator.Pdf;

namespace ReportCreator.Pdf.Tests;

public partial class PdfExportTests
{
    private const string TinyPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Export_RepeatsATextWatermarkWithoutChangingPages()
    {
        await using var exporter = await ReportPdfExporter.LaunchAsync(new PdfExportOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US")
        });
        var token = TestContext.Current.CancellationToken;
        var markedDefinition = LongNotice(DraftMark());
        var plainDefinition = LongNotice(null);
        var disabledDefinition = LongNotice(DraftMark(enabled: false));
        var data = JsonSerializer.SerializeToElement(new { });

        var marked = await exporter.ExportAsync(markedDefinition, data, token);
        var plain = await exporter.ExportAsync(plainDefinition, data, token);
        var disabled = await exporter.ExportAsync(disabledDefinition, data, token);

        var markedPages = ExtractPages(marked);
        var plainPages = ExtractPages(plain);
        Assert.Equal(plainPages.Count, markedPages.Count);
        Assert.Equal(5, markedPages.Count);
        Assert.Equal(markedPages.Count, ExtractPages(disabled).Count);
        Assert.All(markedPages, page => Assert.Contains("DRAFT", page, StringComparison.Ordinal));
        Assert.Contains("Page 1 of 5", string.Concat(markedPages), StringComparison.Ordinal);
        Assert.Contains("Page 5 of 5", string.Concat(markedPages), StringComparison.Ordinal);
        Assert.Contains("Page 1 of 5", string.Concat(plainPages), StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", string.Concat(ExtractPages(disabled)), StringComparison.Ordinal);
        Assert.Equal(0, PageIndex(markedPages, "ANCHOR-MARK"));
        Assert.Equal(markedPages.Count - 1, PageIndex(markedPages, "END-MARK"));
        Assert.Equal(PageIndex(plainPages, "END-MARK"), PageIndex(markedPages, "END-MARK"));
        Assert.DoesNotContain("ANCHOR-MARK", markedPages[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("END-MARK", markedPages[0], StringComparison.Ordinal);

        var markedLines = ExtractPlacedLines(marked);
        var plainLines = ExtractPlacedLines(plain);
        var anchorMarked = Line(markedLines[0], "ANCHOR-MARK");
        var anchorPlain = Line(plainLines[0], "ANCHOR-MARK");
        Assert.InRange(Math.Abs(anchorMarked.X - anchorPlain.X), 0, 1.5);
        Assert.InRange(Math.Abs(anchorMarked.Y - anchorPlain.Y), 0, 1.5);

        var box = MediaBox(marked);
        Assert.All(markedLines, page =>
        {
            Assert.True(TryDraft(page, out var mark), "Missing DRAFT on a page: " + string.Concat(page.Select(line => line.Text)));
            Assert.InRange(mark.CenterX, box.Width / 2 - 140, box.Width / 2 + 140);
            Assert.InRange(mark.Y, box.Height / 2 - 170, box.Height / 2 + 170);
        });

        var end = Line(markedLines[^1], "END-MARK");
        Assert.True(TryDraft(markedLines[^1], out var endMark), "Missing DRAFT on the short page.");
        Assert.True(end.Y > endMark.Y + 40, "The closing line should sit above the centered watermark. Text Y " + end.Y.ToString("0.0", CultureInfo.InvariantCulture) + " watermark Y " + endMark.Y.ToString("0.0", CultureInfo.InvariantCulture));

        var sizes = Regex.Matches(ContentStreams(marked), @"[0-9.]+(?=\s+Tf)").Select(match => match.Value).Distinct().ToArray();
        Assert.Contains(sizes, size => CssPixelsAre(size, 120));

        var corner = await exporter.ExportAsync(LongNotice(DraftMark(horizontal: WatermarkHorizontal.Left, vertical: WatermarkVertical.Top)), data, token);
        var cornerLines = ExtractPlacedLines(corner);
        Assert.All(ExtractPages(corner), page => Assert.Contains("DRAFT", page, StringComparison.Ordinal));
        Assert.True(TryDraft(cornerLines[0], out var topLeft), "Missing the corner watermark.");
        Assert.True(TryDraft(markedLines[0], out var centered), "Missing the centered watermark.");
        Assert.True(centered.X > topLeft.X + 40, "Left watermark X " + topLeft.X.ToString("0.0", CultureInfo.InvariantCulture) + " centered X " + centered.X.ToString("0.0", CultureInfo.InvariantCulture));
        Assert.True(Math.Abs(centered.CenterX - box.Width / 2) + 30 < Math.Abs(topLeft.CenterX - box.Width / 2), "Centered mark " + centered.CenterX.ToString("0.0", CultureInfo.InvariantCulture) + " should sit nearer the middle than " + topLeft.CenterX.ToString("0.0", CultureInfo.InvariantCulture));
        Assert.True(topLeft.Y > centered.Y + 40, "Top watermark Y " + topLeft.Y.ToString("0.0", CultureInfo.InvariantCulture) + " centered Y " + centered.Y.ToString("0.0", CultureInfo.InvariantCulture));

        var rotated = await exporter.ExportAsync(LongNotice(DraftMark(rotation: -30)), data, token);
        Assert.All(ExtractPages(rotated), page => Assert.Contains("DRAFT", page, StringComparison.Ordinal));
        Assert.Equal(markedPages.Count, ExtractPages(rotated).Count);
        var matrices = Regex.Matches(ContentStreams(rotated), @"[-0-9.]+\s+[-0-9.]+\s+[-0-9.]+\s+[-0-9.]+\s+[-0-9.]+\s+[-0-9.]+\s+(?:cm|Tm)");
        Assert.True(matrices.Any(match => Rotated(match.Value)), string.Join(" | ", matrices.Take(8).Select(match => match.Value)));
    }

    [Fact]
    public async Task Export_PrintsTheSampleNoticeOnEveryPage()
    {
        var definition = ReportJson.Read(File.ReadAllText(WatermarkFixture("definition.json")));
        var data = ReportJson.ReadData(File.ReadAllText(WatermarkFixture("data.json")));
        var disabled = ReportJson.Read(ReportJson.Write(definition));
        disabled.Page.Watermark!.Enabled = false;
        await using var exporter = await ReportPdfExporter.LaunchAsync(new PdfExportOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US")
        });
        var token = TestContext.Current.CancellationToken;
        var pdf = await exporter.ExportAsync(definition, data, token);
        var off = await exporter.ExportAsync(disabled, data, token);
        var pages = ExtractPages(pdf);
        var plain = ExtractPages(off);

        Assert.True(pages.Count >= 2, "Expected a multi-page sample, got " + pages.Count + ".");
        Assert.Equal(plain.Count, pages.Count);
        Assert.All(pages, page => Assert.Contains("DRAFT", page, StringComparison.Ordinal));
        Assert.All(pages, page => Assert.Contains("Running sample", page, StringComparison.Ordinal));
        Assert.Contains("First sample line", pages[0], StringComparison.Ordinal);
        Assert.Contains("End of sample", pages[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("End of sample", pages[0], StringComparison.Ordinal);
        Assert.Contains("Page 1 of " + pages.Count.ToString(CultureInfo.InvariantCulture), string.Concat(pages), StringComparison.Ordinal);
        Assert.Contains("Page 1 of " + plain.Count.ToString(CultureInfo.InvariantCulture), string.Concat(plain), StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", string.Concat(plain), StringComparison.Ordinal);

        var lines = ExtractPlacedLines(pdf);
        var box = MediaBox(pdf);
        Assert.All(lines, page =>
        {
            Assert.True(TryDraft(page, out var mark), "Sample page is missing DRAFT.");
            Assert.InRange(mark.CenterX, box.Width / 2 - 140, box.Width / 2 + 140);
        });
        var closing = Line(lines[^1], "End of sample");
        Assert.True(TryDraft(lines[^1], out var closingMark));
        Assert.True(closing.Y > closingMark.Y, "The short final page should keep its line above the watermark.");
    }

    [Fact]
    public async Task Export_PrintsAnImageWatermarkWithoutAddingPages()
    {
        await using var exporter = await ReportPdfExporter.LaunchAsync(new PdfExportOptions
        {
            Culture = CultureInfo.InvariantCulture
        });
        var token = TestContext.Current.CancellationToken;
        var data = JsonSerializer.SerializeToElement(new { });
        var image = new PageWatermark
        {
            Kind = WatermarkKind.Image,
            Source = TinyPng,
            Opacity = 0.35m,
            RotationDegrees = -20,
            WidthMm = 40,
            HeightMm = 40
        };
        var marked = await exporter.ExportAsync(LongNotice(image), data, token);
        var plain = await exporter.ExportAsync(LongNotice(null), data, token);

        Assert.Equal(PageCount(plain), PageCount(marked));
        Assert.Equal(5, PageCount(marked));
        var markedLines = ExtractPlacedLines(marked);
        var plainLines = ExtractPlacedLines(plain);
        var anchorMarked = Line(markedLines[0], "ANCHOR-MARK");
        var anchorPlain = Line(plainLines[0], "ANCHOR-MARK");
        Assert.InRange(Math.Abs(anchorMarked.X - anchorPlain.X), 0, 1.5);
        Assert.InRange(Math.Abs(anchorMarked.Y - anchorPlain.Y), 0, 1.5);
        Assert.DoesNotContain("DRAFT", ExtractText(marked), StringComparison.Ordinal);
        Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(marked), StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype /Image", Encoding.Latin1.GetString(plain), StringComparison.Ordinal);
    }

    private static PageWatermark DraftMark(
        bool enabled = true,
        WatermarkHorizontal horizontal = WatermarkHorizontal.Center,
        WatermarkVertical vertical = WatermarkVertical.Center,
        decimal rotation = 0) => new()
    {
        Enabled = enabled,
        Text = "DRAFT",
        FontFamily = "Segoe UI",
        FontSizePt = 120,
        Color = "#C0C0C0",
        Horizontal = horizontal,
        Vertical = vertical,
        RotationDegrees = rotation
    };

    private static ReportDefinition LongNotice(PageWatermark? watermark) => new()
    {
        Name = "Sample notice",
        Page = new PageSetup
        {
            PageNumbers = PageNumberPlacement.BottomCenter,
            Margin = new Margin { Top = 12, Right = 12, Bottom = 12, Left = 12 },
            Watermark = watermark
        },
        Body =
        {
            Blocks =
            [
                new TextBlock { Id = "anchor", Text = "ANCHOR-MARK" },
                new SpacerBlock { Id = "s1", HeightMm = 200 },
                new SpacerBlock { Id = "s2", HeightMm = 200 },
                new SpacerBlock { Id = "s3", HeightMm = 200 },
                new SpacerBlock { Id = "s4", HeightMm = 200 },
                new TextBlock { Id = "end", Text = "END-MARK", PageBreakBefore = true }
            ]
        }
    };

    private static string WatermarkFixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "watermark", name);

    private static int PageIndex(List<string> pages, string token)
    {
        var index = pages.FindIndex(page => page.Contains(token, StringComparison.Ordinal));
        Assert.True(index >= 0, "Missing " + token + ".");
        return index;
    }

    private static PlacedLine Line(List<PlacedLine> lines, string token)
    {
        var line = lines.FirstOrDefault(item => item.Text.Contains(token, StringComparison.Ordinal));
        Assert.True(line is not null, "Missing placed text " + token + ".");
        return line!;
    }

    private readonly record struct DraftMarkPoint(double X, double Y, double CenterX);

    private static bool TryDraft(List<PlacedLine> lines, out DraftMarkPoint mark)
    {
        var whole = lines.FirstOrDefault(line => line.Text.Contains("DRAFT", StringComparison.Ordinal));
        if (whole is not null)
        {
            mark = new DraftMarkPoint(whole.X, whole.Y, whole.X);
            return true;
        }

        foreach (var band in lines.Where(line => line.Text.Length == 1).GroupBy(line => Math.Round(line.Y)))
        {
            var ordered = band.OrderBy(line => line.X).ToList();
            var word = string.Concat(ordered.Select(line => line.Text));
            var at = word.IndexOf("DRAFT", StringComparison.Ordinal);
            if (at < 0)
                continue;
            var letters = ordered.Skip(at).Take(5).ToList();
            mark = new DraftMarkPoint(letters[0].X, letters.Average(line => line.Y), (letters[0].X + letters[^1].X) / 2);
            return true;
        }

        mark = default;
        return false;
    }

    // A rotated mark shows up as a text matrix whose b and c terms are both set.
    private static bool Rotated(string matrix)
    {
        var parts = matrix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var c))
            return false;
        return Math.Abs(b) > 1 && Math.Abs(c) > 1;
    }

    // Chromium records Tf in CSS pixels. 120pt is 160px at 96dpi.
    private static bool CssPixelsAre(string size, double points)
    {
        if (!double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
            return false;
        return Math.Abs(pixels * 72d / 96d - points) < 1;
    }

    private static string ContentStreams(byte[] pdf)
    {
        var objects = ReadObjects(pdf);
        var builder = new StringBuilder();
        foreach (var content in objects.Values)
        {
            if (content.Data is not null)
                builder.Append(Encoding.Latin1.GetString(content.Data));
        }

        return builder.ToString();
    }
}
