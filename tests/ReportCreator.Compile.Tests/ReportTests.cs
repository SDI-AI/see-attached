using System.Globalization;
using System.Text.Json;
using ReportCreator.Compile;
using ReportCreator.Model;

namespace ReportCreator.Compile.Tests;

public class ReportTests
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Papers_UseTheStandardMillimetreSizes()
    {
        Assert.Equal(new PageSize(297m, 420m), PaperCatalog.Portrait(PaperKind.A3));
        Assert.Equal(new PageSize(210m, 297m), PaperCatalog.Portrait(PaperKind.A4));
        Assert.Equal(new PageSize(148m, 210m), PaperCatalog.Portrait(PaperKind.A5));
        Assert.Equal(new PageSize(215.9m, 279.4m), PaperCatalog.Portrait(PaperKind.Letter));
        Assert.Equal(new PageSize(215.9m, 355.6m), PaperCatalog.Portrait(PaperKind.Legal));
        Assert.Equal(new PageSize(279.4m, 431.8m), PaperCatalog.Portrait(PaperKind.Tabloid));
    }

    [Fact]
    public void Landscape_SwapsWidthAndHeight()
    {
        var size = PaperCatalog.Resolve(new PageSetup
        {
            Paper = PaperKind.Letter,
            Orientation = PageOrientation.Landscape
        });

        Assert.Equal(279.4m, size.WidthMm);
        Assert.Equal(215.9m, size.HeightMm);
    }

    [Fact]
    public void Validator_RejectsUnsafeOrImpreciseReports()
    {
        var report = new ReportDefinition
        {
            SchemaVersion = 2,
            Name = "Bad",
            Page = new PageSetup
            {
                Margin = new Margin { Top = 12.345m, Right = 12, Bottom = 12, Left = 12 }
            },
            Body =
            {
                Blocks =
                [
                    new ImageBlock { Id = "logo", Source = "http://example.com/a.png" },
                    new ColumnsBlock
                    {
                        Id = "outer",
                        Columns =
                        [
                            new Column
                            {
                                Blocks =
                                [
                                    new ColumnsBlock
                                    {
                                        Id = "inner",
                                        Columns = [new Column(), new Column()]
                                    }
                                ]
                            },
                            new Column()
                        ]
                    }
                ]
            }
        };

        var errors = ReportValidator.Validate(report);

        Assert.Contains(errors, error => error.Contains("Schema version", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Top margin", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("https URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("another columns block", StringComparison.Ordinal));
    }

    [Fact]
    public void Invoice_BindsFormatsAndEncodesText()
    {
        var html = CompileInvoice();

        Assert.Contains("@page { size: 210mm 297mm; margin: 12mm 12mm 12mm 12mm; }", html, StringComparison.Ordinal);
        Assert.Contains("font-family:\"Source Sans 3\"", html, StringComparison.Ordinal);
        Assert.Contains("data:font/woff2;base64,", html, StringComparison.Ordinal);
        Assert.Contains("thead { display: table-header-group; }", html, StringComparison.Ordinal);
        Assert.Contains("INV-1001", html, StringComparison.Ordinal);
        Assert.Contains("March 14, 2026", html, StringComparison.Ordinal);
        Assert.Contains("Northwind Traders", html, StringComparison.Ordinal);
        Assert.Contains("Widget", html, StringComparison.Ordinal);
        Assert.Contains("$12.50", html, StringComparison.Ordinal);
        Assert.Contains("$25.00", html, StringComparison.Ordinal);
        Assert.Contains("$65.00", html, StringComparison.Ordinal);
        Assert.Contains("Thank you.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invoice_RoundTripsThroughJson()
    {
        var definition = ReadInvoice();
        var again = ReportJson.Read(ReportJson.Write(definition));
        var first = ReportCompiler.Compile(definition, ReadInvoiceData(), new CompileOptions { Culture = Us });
        var second = ReportCompiler.Compile(again, ReadInvoiceData(), new CompileOptions { Culture = Us });
        Assert.Equal(first, second);
    }

    [Fact]
    public void Text_IsHtmlEncoded()
    {
        var definition = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new TextBlock { Id = "note", Text = "<script>alert(1)</script> {customer.name}" }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new { customer = new { name = "<b>Ada</b>" } });

        var html = ReportCompiler.Compile(definition, data);

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Ada&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesignMode_PostsTheClickedBlockId()
    {
        var definition = new ReportDefinition
        {
            Body = { Blocks = [new TextBlock { Id = "title", Text = "Hello" }] }
        };

        var html = ReportCompiler.Compile(definition, default, new CompileOptions
        {
            DesignMode = true,
            SelectedId = "title"
        });

        Assert.Contains("data-id=\"title\"", html, StringComparison.Ordinal);
        Assert.Contains("rc-selected", html, StringComparison.Ordinal);
        Assert.Contains("body.design .block { cursor: pointer; outline: 1px solid #d1d5db; }", html, StringComparison.Ordinal);
        Assert.Contains("chrome.webview.postMessage", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_ComputesCountAndAverage()
    {
        var averaged = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new TableBlock
                    {
                        Id = "rows",
                        DataPath = "lines",
                        Columns =
                        [
                            new TableColumn { Header = "Note", Path = "note" },
                            new TableColumn { Header = "Qty", Path = "qty", Format = "0", Align = TextAlign.Right }
                        ],
                        Aggregates =
                        [
                            new TableAggregate { Label = "Average", ColumnPath = "qty", Kind = AggregateKind.Avg, Format = "0.00" }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new
        {
            lines = new[] { new { note = "a", qty = 2 }, new { note = "b", qty = 4 } }
        });

        var html = ReportCompiler.Compile(averaged, data, new CompileOptions { Culture = Us });
        Assert.Contains(">Average<", html, StringComparison.Ordinal);
        Assert.Contains(">3.00<", html, StringComparison.Ordinal);

        var counted = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new TableBlock
                    {
                        Id = "rows",
                        DataPath = "lines",
                        Columns = [new TableColumn { Header = "Qty", Path = "qty", Format = "0" }],
                        Aggregates = [new TableAggregate { ColumnPath = "qty", Kind = AggregateKind.Count, Format = "0" }]
                    }
                ]
            }
        };
        var countedHtml = ReportCompiler.Compile(counted, data);
        Assert.Contains("<tfoot><tr><td class=\"left\">2</td></tr></tfoot>", countedHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void LandscapeLetter_SetsTheSwappedPageSize()
    {
        var definition = new ReportDefinition
        {
            Page = new PageSetup { Paper = PaperKind.Letter, Orientation = PageOrientation.Landscape }
        };

        var html = ReportCompiler.Compile(definition, default);

        Assert.Contains("size: 279.4mm 215.9mm;", html, StringComparison.Ordinal);
    }

    private static string CompileInvoice() =>
        ReportCompiler.Compile(ReadInvoice(), ReadInvoiceData(), new CompileOptions { Culture = Us });

    private static ReportDefinition ReadInvoice() =>
        ReportJson.Read(File.ReadAllText(Fixture("definition.json")));

    private static JsonElement ReadInvoiceData() =>
        ReportJson.ReadData(File.ReadAllText(Fixture("data.json")));

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "invoice", name);
}
