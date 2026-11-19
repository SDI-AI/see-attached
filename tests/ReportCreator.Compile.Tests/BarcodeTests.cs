using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReportCreator.Compile;
using ReportCreator.Model;
using ZXing;
using ZXing.Common;

namespace ReportCreator.Compile.Tests;

public class BarcodeTests
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static IEnumerable<object[]> Samples()
    {
        yield return [BarcodeSymbology.Code128, "TRK100000001", "TRK100000001"];
        yield return [BarcodeSymbology.Code39, "INV-2048", "INV-2048"];
        yield return [BarcodeSymbology.QrCode, "SHIP-REF-2048", "SHIP-REF-2048"];
        yield return [BarcodeSymbology.Ean13, "2001234567893", "2001234567893"];
        yield return [BarcodeSymbology.UpcA, "012345678905", "012345678905"];
        yield return [BarcodeSymbology.DataMatrix, "LOT-2048-A", "LOT-2048-A"];
        yield return [BarcodeSymbology.Pdf417, "DOC-2048-SAMPLE", "DOC-2048-SAMPLE"];
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Symbology_RendersAnSvgThatDecodes(BarcodeSymbology symbology, string value, string expected)
    {
        var html = Compile(new BarcodeBlock
        {
            Id = "code",
            Symbology = symbology,
            Value = value,
            ShowText = true,
            WidthMm = symbology is BarcodeSymbology.QrCode or BarcodeSymbology.DataMatrix ? 32 : 60,
            HeightMm = symbology is BarcodeSymbology.QrCode or BarcodeSymbology.DataMatrix ? 32 : 18
        });

        var svg = OneSvg(html);
        Assert.StartsWith("<svg ", svg, StringComparison.Ordinal);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<?xml", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE", svg, StringComparison.Ordinal);
        Assert.Contains("<rect ", svg, StringComparison.Ordinal);
        Assert.Equal(expected, BarcodeDecode.Read(svg, symbology));
        if (symbology is BarcodeSymbology.Code128 or BarcodeSymbology.Code39 or BarcodeSymbology.Ean13 or BarcodeSymbology.UpcA)
            Assert.Contains(">" + expected + "</div>", html, StringComparison.Ordinal);
        else
            Assert.DoesNotContain("<div class=\"barcode-caption\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Ean13_AppendsTheCheckDigitUnderTheBars()
    {
        var html = Compile(new BarcodeBlock
        {
            Id = "ean",
            Symbology = BarcodeSymbology.Ean13,
            Value = "200123456789",
            ShowText = true
        });

        Assert.Contains(">2001234567893</div>", html, StringComparison.Ordinal);
        Assert.Equal("2001234567893", BarcodeDecode.Read(OneSvg(html), BarcodeSymbology.Ean13));
    }

    [Fact]
    public void Binding_UsesThePathAndIgnoresTheLiteral()
    {
        var html = Compile(
            new BarcodeBlock { Id = "code", Path = "shipment.tracking", Value = "LITERAL", ShowText = true },
            new { shipment = new { tracking = "TRK100000001" } });

        Assert.Equal("TRK100000001", BarcodeDecode.Read(OneSvg(html), BarcodeSymbology.Code128));
        Assert.Contains(">TRK100000001</div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("LITERAL", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_ReadsEachRepeatedItem()
    {
        var definition = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new RepeatBlock
                    {
                        Id = "rows",
                        DataPath = "items",
                        Blocks =
                        [
                            new BarcodeBlock { Id = "sku", Path = "item.sku", Symbology = BarcodeSymbology.Code128 }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new { items = new[] { new { sku = "SKU-1001" }, new { sku = "SKU-1002" } } });
        var html = ReportCompiler.Compile(definition, data);
        var svgs = Svgs(html);

        Assert.Equal(2, svgs.Count);
        Assert.Equal("SKU-1001", BarcodeDecode.Read(svgs[0], BarcodeSymbology.Code128));
        Assert.Equal("SKU-1002", BarcodeDecode.Read(svgs[1], BarcodeSymbology.Code128));
    }

    [Fact]
    public void Options_SetColorSizeQuietZoneAndAlignment()
    {
        var wide = Compile(new BarcodeBlock
        {
            Id = "code",
            Value = "TRK100000001",
            Color = "#0B3D91",
            Background = "#F8FAFC",
            Align = TextAlign.Center,
            QuietZone = 0,
            WidthMm = 40,
            HeightMm = 12,
            ShowText = false
        });
        var padded = Compile(new BarcodeBlock
        {
            Id = "code",
            Value = "TRK100000001",
            QuietZone = 10
        });

        var svg = OneSvg(wide);
        Assert.Contains("fill=\"#0B3D91\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#F8FAFC\"", svg, StringComparison.Ordinal);
        Assert.Contains("width=\"40mm\"", svg, StringComparison.Ordinal);
        Assert.Contains("height=\"12mm\"", svg, StringComparison.Ordinal);
        Assert.Contains("margin-left:auto;margin-right:auto", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class=\"barcode-caption\">", wide, StringComparison.Ordinal);
        Assert.True(ViewBoxWidth(padded) > ViewBoxWidth(wide));
        Assert.Equal("TRK100000001", BarcodeDecode.Read(OneSvg(padded), BarcodeSymbology.Code128));
    }

    [Fact]
    public void Qr_ErrorCorrectionChangesTheSymbol()
    {
        var low = OneSvg(Compile(new BarcodeBlock
        {
            Id = "qr",
            Symbology = BarcodeSymbology.QrCode,
            Value = "SHIP-REF-2048-EXTRA",
            ErrorCorrection = QrErrorCorrection.Low,
            ShowText = true
        }));
        var high = OneSvg(Compile(new BarcodeBlock
        {
            Id = "qr",
            Symbology = BarcodeSymbology.QrCode,
            Value = "SHIP-REF-2048-EXTRA",
            ErrorCorrection = QrErrorCorrection.High
        }));

        Assert.NotEqual(Modules(low), Modules(high));
        Assert.Equal("SHIP-REF-2048-EXTRA", BarcodeDecode.Read(low, BarcodeSymbology.QrCode));
        Assert.Equal("SHIP-REF-2048-EXTRA", BarcodeDecode.Read(high, BarcodeSymbology.QrCode));
    }

    [Fact]
    public void Value_IsHtmlEncoded()
    {
        var html = Compile(new BarcodeBlock
        {
            Id = "code",
            Value = "A<B&C",
            ShowText = true
        });

        Assert.Contains(">A&lt;B&amp;C</div>", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Code 128 A&lt;B&amp;C\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("A<B", html, StringComparison.Ordinal);
        Assert.Equal("A<B&C", BarcodeDecode.Read(OneSvg(html), BarcodeSymbology.Code128));
    }

    [Theory]
    [InlineData(BarcodeSymbology.Ean13, "ABC", "not a valid EAN-13")]
    [InlineData(BarcodeSymbology.Ean13, "2001234567890", "not a valid EAN-13")]
    [InlineData(BarcodeSymbology.UpcA, "123", "not a valid UPC-A")]
    [InlineData(BarcodeSymbology.Code39, "inv-2048", "not a valid Code 39")]
    public void InvalidValue_DrawsAPlaceholderAndWarns(BarcodeSymbology symbology, string value, string reason)
    {
        var warnings = new List<string>();
        var html = Compile(new BarcodeBlock { Id = "code", Symbology = symbology, Value = value }, warnings: warnings);

        Assert.Contains("barcode-empty", html, StringComparison.Ordinal);
        Assert.Contains(">Invalid barcode</span>", html, StringComparison.Ordinal);
        Assert.Contains("data-warning=\"", html, StringComparison.Ordinal);
        Assert.Contains(reason, html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        Assert.Contains(warnings, warning => warning.Contains(reason, StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("code", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyBoundValue_DrawsAPlaceholderWithoutFailing()
    {
        var warnings = new List<string>();
        var html = Compile(
            new BarcodeBlock { Id = "track", Path = "shipment.tracking", Symbology = BarcodeSymbology.Code128 },
            new { shipment = new { tracking = "  " } },
            warnings);

        Assert.Contains("barcode-empty", html, StringComparison.Ordinal);
        Assert.Contains("Barcode 'track' has no value.", warnings);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);

        var silent = Compile(
            new BarcodeBlock { Id = "track", Path = "missing.code" },
            new { });
        Assert.Contains("Invalid barcode", silent, StringComparison.Ordinal);
    }

    [Fact]
    public void LongBoundValue_DrawsAPlaceholder()
    {
        var warnings = new List<string>();
        var html = Compile(
            new BarcodeBlock { Id = "code", Path = "code" },
            new { code = new string('A', BarcodeBlock.MaxValueLength + 1) },
            warnings);

        Assert.Contains("value is too long", warnings[0], StringComparison.Ordinal);
        Assert.Contains("barcode-empty", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_RejectsABadDefinitionAndStillAllowsAnEmptyValue()
    {
        var broken = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new BarcodeBlock
                    {
                        Id = "code",
                        Path = "not a path",
                        Color = "blue",
                        Background = "#12",
                        QuietZone = 40,
                        WidthMm = 0,
                        Symbology = (BarcodeSymbology)99
                    }
                ]
            }
        };
        var errors = ReportValidator.Validate(broken);
        Assert.Contains(errors, error => error.Contains("symbology", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("binding path", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("color", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("background", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("quiet zone", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("width", StringComparison.OrdinalIgnoreCase));

        var empty = new ReportDefinition
        {
            Body = { Blocks = [new BarcodeBlock { Id = "code", Value = "" }] }
        };
        Assert.Empty(ReportValidator.Validate(empty));
        Assert.Contains("Invalid barcode", ReportCompiler.Compile(empty, JsonSerializer.SerializeToElement(new { })), StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_BumpsTo6WhenABarcodeIsSavedAndLeavesOlderFilesAlone()
    {
        var definition = new ReportDefinition
        {
            SchemaVersion = 2,
            Name = "Label",
            Page = new PageSetup { Watermark = new PageWatermark { Text = "COPY" } },
            Body = { Blocks = [new BarcodeBlock { Id = "code", Value = "SAMPLE-001" }] }
        };
        var json = ReportJson.Write(definition);
        var again = ReportJson.Read(json);

        Assert.Equal(6, definition.SchemaVersion);
        Assert.Equal(6, again.SchemaVersion);
        var barcode = Assert.IsType<BarcodeBlock>(again.Body.Blocks[0]);
        Assert.Equal(BarcodeSymbology.Code128, barcode.Symbology);
        Assert.Equal("SAMPLE-001", barcode.Value);
        Assert.Equal("COPY", again.Page.Watermark!.Text);

        var watermarkOnly = new ReportDefinition
        {
            SchemaVersion = 4,
            Page = new PageSetup { Watermark = new PageWatermark { Text = "DRAFT" } }
        };
        ReportJson.Write(watermarkOnly);
        Assert.Equal(5, watermarkOnly.SchemaVersion);

        var nested = new ReportDefinition
        {
            SchemaVersion = 1,
            Body =
            {
                Blocks =
                [
                    new PanelBlock
                    {
                        Id = "box",
                        Blocks = [new BarcodeBlock { Id = "code", Value = "SAMPLE-001" }]
                    }
                ]
            }
        };
        ReportJson.Write(nested);
        Assert.Equal(6, nested.SchemaVersion);

        foreach (var sample in new[] { ("invoice", 1), ("orders", 4), ("watermark", 5) })
        {
            var root = Path.Combine(AppContext.BaseDirectory, "fixtures", sample.Item1);
            var report = ReportJson.Read(File.ReadAllText(Path.Combine(root, "definition.json")));
            var data = ReportJson.ReadData(File.ReadAllText(Path.Combine(root, "data.json")));
            Assert.Equal(sample.Item2, report.SchemaVersion);
            Assert.Empty(ReportValidator.Validate(report));
            var html = ReportCompiler.Compile(report, data);
            Assert.DoesNotContain("class=\"barcode\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("barcode-empty", html, StringComparison.Ordinal);
            ReportJson.Write(report);
            Assert.Equal(sample.Item2, report.SchemaVersion);
        }
    }

    [Fact]
    public void Sample_OpensAndPrintsEverySymbology()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "barcode");
        var definition = ReportJson.Read(File.ReadAllText(Path.Combine(root, "definition.json")));
        var data = ReportJson.ReadData(File.ReadAllText(Path.Combine(root, "data.json")));
        Assert.Equal(6, definition.SchemaVersion);
        Assert.Empty(ReportValidator.Validate(definition));

        var warnings = new List<string>();
        var html = ReportCompiler.Compile(definition, data, new CompileOptions { Culture = Us, Warnings = warnings });
        var again = ReportJson.Read(ReportJson.Write(definition));

        Assert.Equal(6, again.SchemaVersion);
        Assert.Empty(warnings);
        Assert.Contains("Sample Recipient", html, StringComparison.Ordinal);
        Assert.Contains("Example City", html, StringComparison.Ordinal);
        Assert.Contains("April 2, 2026", html, StringComparison.Ordinal);
        Assert.Contains(">INV-2048</div>", html, StringComparison.Ordinal);
        Assert.Equal(7, Svgs(html).Count);
        Assert.Equal("TRK100000001", BarcodeDecode.Read(Svgs(html)[0], BarcodeSymbology.Code128));
        Assert.Equal("SHIP-REF-2048", BarcodeDecode.Read(Svgs(html)[4], BarcodeSymbology.QrCode));
        Assert.Contains(ReportBindings.List(definition), binding => binding.Path == "shipment.tracking");
    }

    [Fact]
    public void Json_RoundTripsTheOptionSet()
    {
        const string json = """
            {
              "schemaVersion": 6,
              "name": "Label",
              "body": {
                "blocks": [
                  {
                    "type": "barcode",
                    "id": "qr",
                    "symbology": "qrCode",
                    "path": "shipment.reference",
                    "value": "ignored",
                    "errorCorrection": "high",
                    "quietZone": 2,
                    "color": "#111827",
                    "background": "#ffffff",
                    "align": "right",
                    "showText": true,
                    "widthMm": 28,
                    "heightMm": 28
                  }
                ]
              }
            }
            """;

        var definition = ReportJson.Read(json);
        var barcode = Assert.IsType<BarcodeBlock>(definition.Body.Blocks[0]);
        Assert.Equal(BarcodeSymbology.QrCode, barcode.Symbology);
        Assert.Equal("shipment.reference", barcode.Path);
        Assert.Equal(QrErrorCorrection.High, barcode.ErrorCorrection);
        Assert.Equal(2, barcode.QuietZone);
        Assert.Equal(TextAlign.Right, barcode.Align);
        Assert.True(barcode.ShowText);
        Assert.Equal(28m, barcode.WidthMm);

        var saved = ReportJson.Read(ReportJson.Write(definition));
        var again = Assert.IsType<BarcodeBlock>(saved.Body.Blocks[0]);
        Assert.Equal(QrErrorCorrection.High, again.ErrorCorrection);
        Assert.Equal("#ffffff", again.Background);
    }

    private static string Compile(BarcodeBlock barcode, object? data = null, ICollection<string>? warnings = null)
    {
        var definition = new ReportDefinition
        {
            Body = { Blocks = [barcode] }
        };
        return ReportCompiler.Compile(definition, JsonSerializer.SerializeToElement(data ?? new { }), new CompileOptions
        {
            Warnings = warnings
        });
    }

    private static string OneSvg(string html)
    {
        var svgs = Svgs(html);
        Assert.Single(svgs);
        return svgs[0];
    }

    private static List<string> Svgs(string html)
    {
        var found = new List<string>();
        var index = 0;
        while (true)
        {
            var start = html.IndexOf("<svg ", index, StringComparison.Ordinal);
            if (start < 0)
                break;
            var end = html.IndexOf("</svg>", start, StringComparison.Ordinal);
            Assert.True(end > start);
            found.Add(html[start..(end + "</svg>".Length)]);
            index = end + "</svg>".Length;
        }

        return found;
    }

    private static int ViewBoxWidth(string html)
    {
        var match = Regex.Match(OneSvg(html), "viewBox=\"0 0 (\\d+) ");
        Assert.True(match.Success);
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string Modules(string svg) =>
        string.Concat(Regex.Matches(svg, "<rect x=\"\\d+\"").Select(match => match.Value));
}

file static class BarcodeDecode
{
    public static string Read(string svg, BarcodeSymbology symbology)
    {
        var viewBox = Regex.Match(svg, "viewBox=\"0 0 (\\d+) (\\d+)\"");
        if (!viewBox.Success)
            throw new InvalidOperationException("The SVG has no viewBox.");

        var width = int.Parse(viewBox.Groups[1].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(viewBox.Groups[2].Value, CultureInfo.InvariantCulture);
        var cells = new bool[width, height];
        foreach (Match rect in Regex.Matches(svg, "<rect x=\"(\\d+)\" y=\"(\\d+)\" width=\"(\\d+)\" height=\"(\\d+)\"/>"))
        {
            var x = int.Parse(rect.Groups[1].Value, CultureInfo.InvariantCulture);
            var y = int.Parse(rect.Groups[2].Value, CultureInfo.InvariantCulture);
            var rectWidth = int.Parse(rect.Groups[3].Value, CultureInfo.InvariantCulture);
            var rectHeight = int.Parse(rect.Groups[4].Value, CultureInfo.InvariantCulture);
            for (var row = y; row < y + rectHeight && row < height; row++)
            {
                for (var column = x; column < x + rectWidth && column < width; column++)
                    cells[column, row] = true;
            }
        }

        const int module = 4;
        var pixelWidth = width * module;
        var pixelHeight = height == 1 ? 80 : height * module;
        var bytes = new byte[pixelWidth * pixelHeight * 3];
        Array.Fill(bytes, (byte)255);
        for (var y = 0; y < pixelHeight; y++)
        {
            var sourceY = height == 1 ? 0 : y / module;
            for (var x = 0; x < pixelWidth; x++)
            {
                if (!cells[x / module, sourceY])
                    continue;
                var offset = (y * pixelWidth + x) * 3;
                bytes[offset] = 0;
                bytes[offset + 1] = 0;
                bytes[offset + 2] = 0;
            }
        }

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions
            {
                PossibleFormats = new List<BarcodeFormat> { Format(symbology) },
                TryHarder = true,
                PureBarcode = true
            }
        };
        var result = reader.Decode(bytes, pixelWidth, pixelHeight, RGBLuminanceSource.BitmapFormat.RGB24);
        Assert.NotNull(result);
        return result.Text;
    }

    private static BarcodeFormat Format(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.Code39 => BarcodeFormat.CODE_39,
        BarcodeSymbology.QrCode => BarcodeFormat.QR_CODE,
        BarcodeSymbology.Ean13 => BarcodeFormat.EAN_13,
        BarcodeSymbology.UpcA => BarcodeFormat.UPC_A,
        BarcodeSymbology.DataMatrix => BarcodeFormat.DATA_MATRIX,
        BarcodeSymbology.Pdf417 => BarcodeFormat.PDF_417,
        _ => BarcodeFormat.CODE_128
    };
}
