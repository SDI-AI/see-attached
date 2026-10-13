using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReportCreator.Model;
using ReportCreator.Pdf;

namespace ReportCreator.Pdf.Tests;

public class PdfExportTests
{
    [Fact]
    public async Task Export_UsesThePaperSizeAndContainsTheInvoiceText()
    {
        var definition = ReportJson.Read(File.ReadAllText(Fixture("definition.json")));
        var data = ReportJson.ReadData(File.ReadAllText(Fixture("data.json")));
        await using var exporter = await ReportPdfExporter.LaunchAsync(new PdfExportOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US")
        });

        var pdf = await exporter.ExportAsync(definition, data, TestContext.Current.CancellationToken);

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf[..5]), StringComparison.Ordinal);
        Assert.Equal(1, PageCount(pdf));
        var box = MediaBox(pdf);
        Assert.InRange(box.Width, 594, 597);
        Assert.InRange(box.Height, 840, 843);
        var text = ExtractText(pdf);
        Assert.Contains("Northwind Traders", text, StringComparison.Ordinal);
        Assert.Contains("INV-1001", text, StringComparison.Ordinal);
        Assert.Contains("Widget", text, StringComparison.Ordinal);

        var landscape = new ReportDefinition
        {
            Name = "Landscape",
            Page = new PageSetup
            {
                Paper = PaperKind.Letter,
                Orientation = PageOrientation.Landscape,
                Margin = new Margin { Top = 10, Right = 10, Bottom = 10, Left = 10 }
            },
            Body = { Blocks = [new TextBlock { Id = "title", Text = "Wide" }] }
        };
        var wide = await exporter.ExportAsync(landscape, data, TestContext.Current.CancellationToken);
        var wideBox = MediaBox(wide);
        Assert.InRange(wideBox.Width, 790, 793);
        Assert.InRange(wideBox.Height, 611, 614);
    }

    [Fact]
    public async Task Export_SplitsALongTableAcrossPagesWithoutCuttingARow()
    {
        var rows = new List<object>();
        for (var index = 1; index <= 48; index++)
        {
            rows.Add(index == 30
                ? new { description = "ROW-KEEP-TOGETHER", amount = 4242 }
                : new { description = "Line " + index.ToString(CultureInfo.InvariantCulture), amount = index });
        }

        var definition = new ReportDefinition
        {
            Name = "Statement",
            Page = new PageSetup
            {
                PageNumbers = PageNumberPlacement.BottomCenter,
                PageHeaderMm = 12,
                PageFooterMm = 10
            },
            PageHeader = { Blocks = [new TextBlock { Id = "running", Text = "Acme Statement" }] },
            PageFooter = { Blocks = [new TextBlock { Id = "copy", Text = "Keep this copy" }] },
            Body =
            {
                Blocks =
                [
                    new TableBlock
                    {
                        Id = "lines",
                        DataPath = "lines",
                        Columns =
                        [
                            new TableColumn { Header = "Description", Path = "description" },
                            new TableColumn { Header = "Amount", Path = "amount", Align = TextAlign.Right }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new { lines = rows });
        await using var exporter = await ReportPdfExporter.LaunchAsync(new PdfExportOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US")
        });

        var pdf = await exporter.ExportAsync(definition, data, TestContext.Current.CancellationToken);
        var pages = ExtractPages(pdf);

        Assert.True(pages.Count >= 2, "Expected the statement to fill at least two pages, got " + pages.Count + ".");
        Assert.Contains(pages, page => page.Contains("Page 1 of", StringComparison.Ordinal));
        Assert.Contains(pages, page => page.Contains("Page 2 of", StringComparison.Ordinal));
        Assert.All(pages, page => Assert.Contains("Acme Statement", page, StringComparison.Ordinal));
        Assert.All(pages, page => Assert.Contains("Description", page, StringComparison.Ordinal));
        Assert.All(pages, page => Assert.Contains("Keep this copy", page, StringComparison.Ordinal));
        Assert.Single(pages, page => page.Contains("ROW-KEEP-TOGETHER", StringComparison.Ordinal));
        Assert.Contains(
            "4242",
            pages.Single(page => page.Contains("ROW-KEEP-TOGETHER", StringComparison.Ordinal)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_OmitsThePageNumberOnTheFirstPageOnly()
    {
        var rows = new List<object>();
        for (var index = 1; index <= 48; index++)
            rows.Add(new { description = "Line " + index.ToString(CultureInfo.InvariantCulture), amount = index });

        var definition = new ReportDefinition
        {
            Name = "Statement",
            Page = new PageSetup
            {
                PageNumbers = PageNumberPlacement.BottomRight,
                PageHeaderMm = 12,
                OmitPageNumberOnFirstPage = true
            },
            PageHeader = { Blocks = [new TextBlock { Id = "running", Text = "Acme Statement" }] },
            Body =
            {
                Blocks =
                [
                    new TableBlock
                    {
                        Id = "lines",
                        DataPath = "lines",
                        Columns =
                        [
                            new TableColumn { Header = "Description", Path = "description" },
                            new TableColumn { Header = "Amount", Path = "amount", Align = TextAlign.Right }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new { lines = rows });
        await using var exporter = await ReportPdfExporter.LaunchAsync(new PdfExportOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US")
        });

        var pages = ExtractPages(await exporter.ExportAsync(definition, data, TestContext.Current.CancellationToken));

        Assert.True(pages.Count >= 2, "Expected the statement to fill at least two pages, got " + pages.Count + ".");
        Assert.DoesNotContain("Page 1 of", pages[0], StringComparison.Ordinal);
        Assert.Contains("Page 2 of", pages[1], StringComparison.Ordinal);
        Assert.Contains("Acme Statement", pages[0], StringComparison.Ordinal);
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "invoice", name);

    private static int PageCount(byte[] pdf)
    {
        var latin = Encoding.Latin1.GetString(pdf);
        return Regex.Matches(latin, @"/Type\s*/Page(?!s)").Count;
    }

    private static (double Width, double Height) MediaBox(byte[] pdf)
    {
        var latin = Encoding.Latin1.GetString(pdf);
        var match = Regex.Match(latin, @"/MediaBox\s*\[\s*0\s+0\s+([0-9.]+)\s+([0-9.]+)\s*\]");
        Assert.True(match.Success, "The PDF has no MediaBox.");
        return (
            double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private sealed record PdfObject(string Body, byte[]? Data);

    private sealed class GlyphMap
    {
        public int HexWidth { get; private set; } = 4;

        public Dictionary<int, string> Characters { get; } = [];

        public void Add(string glyphHex, string characters)
        {
            HexWidth = glyphHex.Length;
            Characters[Convert.ToInt32(glyphHex, 16)] = characters;
        }
    }

    // Chromium writes one subset font per face. Each face has its own ToUnicode
    // CMap, and the same glyph id means different characters in each face.
    private static string ExtractText(byte[] pdf) => string.Concat(ExtractPages(pdf));

    private static List<string> ExtractPages(byte[] pdf)
    {
        var objects = ReadObjects(pdf);
        var pages = new List<string>();
        foreach (var page in OrderedPages(objects))
        {
            var text = new StringBuilder();
            var fonts = FontMaps(objects, ResourceBody(objects, page.Body));
            foreach (var contentId in ContentIds(page.Body))
            {
                if (objects.TryGetValue(contentId, out var content) && content.Data is not null)
                    AppendShownText(Encoding.Latin1.GetString(content.Data), fonts, text);
            }

            pages.Add(text.ToString());
        }

        return pages;
    }

    private static IEnumerable<PdfObject> OrderedPages(Dictionary<int, PdfObject> objects)
    {
        var catalog = objects.Values.FirstOrDefault(item => Regex.IsMatch(item.Body, @"/Type\s*/Catalog\b"));
        var root = catalog is null ? null : Regex.Match(catalog.Body, @"/Pages\s+(?<id>\d+)\s+\d+\s+R");
        if (root is not { Success: true }
            || !int.TryParse(root.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var rootId))
        {
            return objects.Values.Where(item => Regex.IsMatch(item.Body, @"/Type\s*/Page(?!s)"));
        }

        var ordered = new List<PdfObject>();
        WalkPages(objects, rootId, ordered);
        return ordered;
    }

    private static void WalkPages(Dictionary<int, PdfObject> objects, int id, List<PdfObject> ordered)
    {
        if (!objects.TryGetValue(id, out var item))
            return;
        if (Regex.IsMatch(item.Body, @"/Type\s*/Pages\b"))
        {
            var kids = Regex.Match(item.Body, @"/Kids\s*\[(?<ids>[^\]]*)\]");
            if (!kids.Success)
                return;
            foreach (Match kid in Regex.Matches(kids.Groups["ids"].Value, @"(?<id>\d+)\s+\d+\s+R"))
            {
                if (int.TryParse(kid.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var child))
                    WalkPages(objects, child, ordered);
            }

            return;
        }

        if (Regex.IsMatch(item.Body, @"/Type\s*/Page(?!s)"))
            ordered.Add(item);
    }

    private static Dictionary<int, PdfObject> ReadObjects(byte[] pdf)
    {
        var objects = new Dictionary<int, PdfObject>();
        var index = 0;
        while (index < pdf.Length)
        {
            var marker = IndexOf(pdf, " obj"u8, index);
            if (marker < 0)
                break;

            var lineStart = marker;
            while (lineStart > 0 && pdf[lineStart - 1] is not (byte)'\n' and not (byte)'\r')
                lineStart--;

            var header = Encoding.ASCII.GetString(pdf[lineStart..marker]);
            var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || !int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                index = marker + 4;
                continue;
            }

            var bodyStart = marker + 4;
            var endObj = IndexOf(pdf, "endobj"u8, bodyStart);
            if (endObj < 0)
                break;

            var streamAt = IndexOf(pdf, "stream"u8, bodyStart);
            byte[]? data = null;
            var bodyEnd = endObj;
            if (streamAt >= 0 && streamAt < endObj)
            {
                bodyEnd = streamAt;
                var dataAt = streamAt + "stream".Length;
                if (dataAt < pdf.Length && pdf[dataAt] == '\r')
                    dataAt++;
                if (dataAt < pdf.Length && pdf[dataAt] == '\n')
                    dataAt++;
                var endStream = IndexOf(pdf, "endstream"u8, dataAt);
                if (endStream < 0)
                    break;

                var raw = pdf[dataAt..endStream];
                while (raw.Length > 0 && raw[^1] is (byte)'\n' or (byte)'\r')
                    raw = raw[..^1];
                data = Inflate(raw);
                endObj = IndexOf(pdf, "endobj"u8, endStream);
                if (endObj < 0)
                    break;
            }

            objects[id] = new PdfObject(Encoding.Latin1.GetString(pdf[bodyStart..bodyEnd]), data);
            index = endObj + "endobj".Length;
        }

        return objects;
    }

    private static byte[]? Inflate(byte[] raw)
    {
        try
        {
            using var input = new MemoryStream(raw);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return raw;
        }
        catch (IOException)
        {
            return raw;
        }
    }

    private static string ResourceBody(Dictionary<int, PdfObject> objects, string pageBody)
    {
        var indirect = Regex.Match(pageBody, @"/Resources\s+(?<id>\d+)\s+\d+\s+R");
        if (indirect.Success
            && int.TryParse(indirect.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            && objects.TryGetValue(id, out var resources))
            return resources.Body;

        return pageBody;
    }

    private static Dictionary<string, GlyphMap> FontMaps(Dictionary<int, PdfObject> objects, string resources)
    {
        var maps = new Dictionary<string, GlyphMap>(StringComparer.Ordinal);
        var fontDictionary = Regex.Match(resources, @"/Font\s*<<(?<body>.*?)>>", RegexOptions.Singleline);
        if (!fontDictionary.Success)
            return maps;

        foreach (Match name in Regex.Matches(fontDictionary.Groups["body"].Value, @"/(?<name>[A-Za-z][A-Za-z0-9]*)\s+(?<id>\d+)\s+\d+\s+R"))
        {
            if (!int.TryParse(name.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var fontId)
                || !objects.TryGetValue(fontId, out var font))
                continue;

            var toUnicode = Regex.Match(font.Body, @"/ToUnicode\s+(?<id>\d+)\s+\d+\s+R");
            if (!toUnicode.Success
                || !int.TryParse(toUnicode.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var cmapId)
                || !objects.TryGetValue(cmapId, out var cmap)
                || cmap.Data is null)
                continue;

            maps[name.Groups["name"].Value] = ParseCMap(Encoding.Latin1.GetString(cmap.Data));
        }

        return maps;
    }

    private static IEnumerable<int> ContentIds(string pageBody)
    {
        var array = Regex.Match(pageBody, @"/Contents\s*\[(?<ids>[^\]]*)\]");
        if (array.Success)
        {
            foreach (Match id in Regex.Matches(array.Groups["ids"].Value, @"(?<id>\d+)\s+\d+\s+R"))
            {
                if (int.TryParse(id.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var contentId))
                    yield return contentId;
            }

            yield break;
        }

        var single = Regex.Match(pageBody, @"/Contents\s+(?<id>\d+)\s+\d+\s+R");
        if (single.Success && int.TryParse(single.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var only))
            yield return only;
    }

    private static GlyphMap ParseCMap(string cmap)
    {
        var map = new GlyphMap();
        foreach (Match block in Regex.Matches(cmap, @"beginbfchar\s*(?<body>.*?)\s*endbfchar", RegexOptions.Singleline))
        {
            foreach (Match pair in Regex.Matches(block.Groups["body"].Value, @"<(?<glyph>[0-9A-Fa-f]+)>\s*<(?<unicode>[0-9A-Fa-f]+)>"))
                map.Add(pair.Groups["glyph"].Value, DecodeUnicode(pair.Groups["unicode"].Value));
        }

        foreach (Match block in Regex.Matches(cmap, @"beginbfrange\s*(?<body>.*?)\s*endbfrange", RegexOptions.Singleline))
        {
            var body = block.Groups["body"].Value;
            foreach (Match range in Regex.Matches(body, @"<(?<start>[0-9A-Fa-f]+)>\s*<(?<end>[0-9A-Fa-f]+)>\s*<(?<unicode>[0-9A-Fa-f]+)>"))
            {
                var start = Convert.ToInt32(range.Groups["start"].Value, 16);
                var end = Convert.ToInt32(range.Groups["end"].Value, 16);
                var unicode = Convert.ToInt32(range.Groups["unicode"].Value, 16);
                var width = range.Groups["start"].Value.Length;
                for (var offset = 0; start + offset <= end; offset++)
                    map.Add((start + offset).ToString("X" + width, CultureInfo.InvariantCulture), char.ConvertFromUtf32(unicode + offset));
            }

            foreach (Match range in Regex.Matches(body, @"<(?<start>[0-9A-Fa-f]+)>\s*<(?<end>[0-9A-Fa-f]+)>\s*\[(?<list>[^\]]*)\]"))
            {
                var start = Convert.ToInt32(range.Groups["start"].Value, 16);
                var width = range.Groups["start"].Value.Length;
                var destinations = Regex.Matches(range.Groups["list"].Value, @"<(?<unicode>[0-9A-Fa-f]+)>");
                for (var offset = 0; offset < destinations.Count; offset++)
                    map.Add((start + offset).ToString("X" + width, CultureInfo.InvariantCulture), DecodeUnicode(destinations[offset].Groups["unicode"].Value));
            }
        }

        return map;
    }

    private static string DecodeUnicode(string hex)
    {
        if (hex.Length % 2 != 0)
            hex = "0" + hex;
        var bytes = Convert.FromHexString(hex);
        if (bytes.Length % 2 != 0)
            bytes = [0, .. bytes];
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    private static void AppendShownText(string source, Dictionary<string, GlyphMap> fonts, StringBuilder text)
    {
        GlyphMap? current = null;
        foreach (Match token in Regex.Matches(source, @"/(?<font>[A-Za-z][A-Za-z0-9]*)\s+[0-9.]+\s+Tf|<(?<hex>[0-9A-Fa-f]+)>\s*Tj"))
        {
            if (token.Groups["font"].Success)
            {
                fonts.TryGetValue(token.Groups["font"].Value, out current);
                continue;
            }

            if (current is null)
                continue;

            var hex = token.Groups["hex"].Value;
            var width = current.HexWidth;
            for (var index = 0; index + width <= hex.Length; index += width)
            {
                var glyph = Convert.ToInt32(hex.Substring(index, width), 16);
                if (current.Characters.TryGetValue(glyph, out var characters))
                    text.Append(characters);
            }
        }
    }

    private static int IndexOf(byte[] buffer, ReadOnlySpan<byte> pattern, int start)
    {
        var span = buffer.AsSpan(start);
        var found = span.IndexOf(pattern);
        return found < 0 ? -1 : start + found;
    }
}
