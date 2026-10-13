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

    [Fact]
    public void PageChrome_RepeatsHeaderAndPrintsPageNumbers()
    {
        var definition = new ReportDefinition
        {
            Page = new PageSetup { PageNumbers = PageNumberPlacement.BottomCenter, PageHeaderMm = 12, PageFooterMm = 10 },
            PageHeader = { Blocks = [new TextBlock { Id = "running", Text = "Acme" }] },
            PageFooter = { Blocks = [new TextBlock { Id = "copy", Text = "File copy" }] },
            Body = { Blocks = [new TextBlock { Id = "body", Text = "Body" }] }
        };

        var html = ReportCompiler.Compile(definition, default);

        Assert.Contains("class=\"page-chrome page-header\"", html, StringComparison.Ordinal);
        Assert.Contains(">Acme<", html, StringComparison.Ordinal);
        Assert.Contains(">File copy<", html, StringComparison.Ordinal);
        Assert.Contains("position: fixed", html, StringComparison.Ordinal);
        Assert.Contains("counter(page)", html, StringComparison.Ordinal);
        Assert.Contains("counter(pages)", html, StringComparison.Ordinal);
        Assert.Contains("margin: 24mm 12mm 28mm 12mm;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("@page :first", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Parameters_FormatInTheCultureAndFillFromTheDefault()
    {
        var definition = new ReportDefinition
        {
            Name = "A&B",
            Parameters =
            [
                new ReportParameter { Name = "amount", Kind = ParameterKind.Number, Default = "1.5" },
                new ReportParameter { Name = "title", Kind = ParameterKind.String, Default = "Estimate" },
                new ReportParameter { Name = "when", Kind = ParameterKind.Date, Default = "2026-03-14" },
                new ReportParameter { Name = "note", Kind = ParameterKind.String }
            ],
            Body =
            {
                Blocks =
                [
                    new TextBlock
                    {
                        Id = "body",
                        Text = "{param.amount:C}\n{param.title}\n{param.when:MMMM d, yyyy}\n{param.note}\n{report.name}"
                    }
                ]
            }
        };

        var defaults = ReportCompiler.Compile(definition, default, new CompileOptions { Culture = Us });
        Assert.Contains("$1.50", defaults, StringComparison.Ordinal);
        Assert.Contains("Estimate", defaults, StringComparison.Ordinal);
        Assert.Contains("March 14, 2026", defaults, StringComparison.Ordinal);
        Assert.Contains("A&amp;B", defaults, StringComparison.Ordinal);
        Assert.DoesNotContain("parameters", ReportJson.Write(new ReportDefinition { Name = "Plain" }), StringComparison.Ordinal);

        var supplied = ReportCompiler.Compile(definition, default, new CompileOptions
        {
            Culture = Us,
            Parameters = JsonSerializer.SerializeToElement(new { amount = 2.25m, title = "", note = "<b>" })
        });
        Assert.Contains("$2.25", supplied, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;", supplied, StringComparison.Ordinal);
        Assert.DoesNotContain("Estimate", supplied, StringComparison.Ordinal);
        Assert.Contains("March 14, 2026", supplied, StringComparison.Ordinal);

        var sample = ReportBindings.Sample(definition);
        Assert.False(sample.TryGetProperty("param", out _));
        Assert.False(sample.TryGetProperty("report", out _));
        Assert.False(sample.TryGetProperty("print", out _));
        Assert.Contains("\"kind\": \"number\"", ReportJson.Write(definition), StringComparison.Ordinal);
    }

    [Fact]
    public void PrintDate_UsesTheClockPassedToCompile()
    {
        var definition = new ReportDefinition
        {
            Name = "Statement",
            Body =
            {
                Blocks =
                [
                    new TextBlock { Id = "clock", Text = "{print.time:HH:mm}" },
                    new FieldBlock { Id = "day", Path = "print.date", Format = "yyyy-MM-dd" }
                ]
            }
        };

        var html = ReportCompiler.Compile(definition, default, new CompileOptions
        {
            Culture = CultureInfo.InvariantCulture,
            Clock = new DateTimeOffset(2026, 3, 14, 15, 4, 0, TimeSpan.Zero)
        });

        Assert.Contains("15:04", html, StringComparison.Ordinal);
        Assert.Contains("2026-03-14", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ParameterToken_DoesNotStealALongerDataPath()
    {
        var definition = new ReportDefinition
        {
            Parameters = [new ReportParameter { Name = "customer", Kind = ParameterKind.String, Default = "Defaulted" }],
            Body = { Blocks = [new TextBlock { Id = "t", Text = "{param.customer} {param.customer.name}" }] }
        };
        var data = JsonSerializer.SerializeToElement(new { param = new { customer = new { name = "Kept" } } });

        var html = ReportCompiler.Compile(definition, data);

        Assert.Contains("Defaulted", html, StringComparison.Ordinal);
        Assert.Contains("Kept", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PageNumbers_CanStayOffTheFirstPage()
    {
        var definition = new ReportDefinition
        {
            Page = new PageSetup
            {
                PageNumbers = PageNumberPlacement.BottomCenter,
                PageHeaderMm = 12,
                OmitPageNumberOnFirstPage = true
            },
            PageHeader = { Blocks = [new TextBlock { Id = "running", Text = "Acme" }] },
            Body = { Blocks = [new TextBlock { Id = "body", Text = "Body" }] }
        };

        var html = ReportCompiler.Compile(definition, default);

        Assert.Contains("@page :first { @bottom-center { content: none; } }", html, StringComparison.Ordinal);
        Assert.Contains("class=\"page-chrome page-header\"", html, StringComparison.Ordinal);
        Assert.Contains(">Acme<", html, StringComparison.Ordinal);
        Assert.Contains("counter(page)", html, StringComparison.Ordinal);
        Assert.True(ReportJson.Read(ReportJson.Write(definition)).Page.OmitPageNumberOnFirstPage);
    }

    [Fact]
    public void Validator_RejectsAParameterThatDoesNotMatch()
    {
        var report = new ReportDefinition
        {
            Parameters = [new ReportParameter { Name = "copies", Kind = ParameterKind.Number, Default = "tomorrow" }],
            Body = { Blocks = [new TextBlock { Id = "t", Text = "{param.missing}" }] }
        };

        var errors = ReportValidator.Validate(report);

        Assert.Contains(errors, error => error.Contains("copies", StringComparison.Ordinal) && error.Contains("number", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("not declared", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowWhen_HidesAndCompares()
    {
        var definition = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new TextBlock { Id = "shown", Text = "Visible note", When = new ShowWhen { Path = "note", Kind = ShowWhenKind.NotEmpty } },
                    new TextBlock { Id = "hidden", Text = "Secret", When = new ShowWhen { Path = "secret", Kind = ShowWhenKind.NotEmpty } },
                    new TextBlock { Id = "match", Text = "Two items", When = new ShowWhen { Path = "qty", Kind = ShowWhenKind.Equals, Value = "2" } },
                    new TextBlock { Id = "blank", Text = "No secret", When = new ShowWhen { Path = "secret", Kind = ShowWhenKind.Empty } }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new { note = "Hi", qty = 2 });

        var html = ReportCompiler.Compile(definition, data);

        Assert.Contains("Visible note", html, StringComparison.Ordinal);
        Assert.Contains("Two items", html, StringComparison.Ordinal);
        Assert.Contains("No secret", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_GroupsRowsAndCarriesARunningTotal()
    {
        var definition = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new TableBlock
                    {
                        Id = "lines",
                        DataPath = "lines",
                        GroupBy = "customer",
                        Columns =
                        [
                            new TableColumn { Header = "Customer", Path = "customer" },
                            new TableColumn { Header = "Amount", Path = "amount", Format = "0.00", Align = TextAlign.Right, RunningTotal = true }
                        ],
                        GroupAggregates = [new TableAggregate { Label = "Subtotal", ColumnPath = "amount", Kind = AggregateKind.Sum, Format = "0.00" }],
                        Aggregates = [new TableAggregate { Label = "Total", ColumnPath = "amount", Kind = AggregateKind.Sum, Format = "0.00" }]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new
        {
            lines = new[]
            {
                new { customer = "Ada", amount = 1 },
                new { customer = "Ada", amount = 2 },
                new { customer = "Bea", amount = 4 }
            }
        });

        var html = ReportCompiler.Compile(definition, data, new CompileOptions { Culture = CultureInfo.InvariantCulture });

        Assert.Contains(">Ada<", html, StringComparison.Ordinal);
        Assert.Contains(">Bea<", html, StringComparison.Ordinal);
        Assert.Contains(">1.00<", html, StringComparison.Ordinal);
        Assert.Contains(">3.00<", html, StringComparison.Ordinal);
        Assert.Contains(">4.00<", html, StringComparison.Ordinal);
        Assert.Contains(">Subtotal<", html, StringComparison.Ordinal);
        Assert.Contains(">7.00<", html, StringComparison.Ordinal);
        Assert.Contains("group-head", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("<table class=\"group\">", StringSplitOptions.None).Length - 1);
        Assert.Contains("<th colspan=\"2\">Ada</th>", html, StringComparison.Ordinal);
        Assert.Contains("<th colspan=\"2\">Bea</th>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PageBreaks_MarkTheBlockAndEachLaterGroup()
    {
        var definition = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new TextBlock { Id = "note", Text = "Cover", PageBreakAfter = true },
                    new TableBlock
                    {
                        Id = "lines",
                        DataPath = "lines",
                        GroupBy = "customer",
                        PageBreakBefore = true,
                        BreakBeforeGroup = true,
                        KeepGroupTogether = true,
                        Columns =
                        [
                            new TableColumn { Header = "Customer", Path = "customer" },
                            new TableColumn { Header = "Amount", Path = "amount", Format = "0.00" }
                        ],
                        Aggregates = [new TableAggregate { Label = "Total", ColumnPath = "amount", Kind = AggregateKind.Sum, Format = "0.00" }]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new
        {
            lines = new[]
            {
                new { customer = "Ada", amount = 1 },
                new { customer = "Bea", amount = 2 }
            }
        });

        var html = ReportCompiler.Compile(definition, data, new CompileOptions { Culture = CultureInfo.InvariantCulture });

        Assert.Contains("break-before: page", html, StringComparison.Ordinal);
        Assert.Contains("page-break-before: always", html, StringComparison.Ordinal);
        Assert.Contains("break-after: page", html, StringComparison.Ordinal);
        Assert.Contains("class=\"block break-after\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"block groups break-before\"", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"group-wrap keep\"><table class=\"group\">", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"group-wrap keep break-before\"><table class=\"group\">", html, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"group-head\"><th colspan=\"2\">Ada</th></tr><tr><th class=\"left\">Customer</th>", html, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"group-head\"><th colspan=\"2\">Bea</th></tr><tr><th class=\"left\">Customer</th>", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split(">Total<", StringSplitOptions.None).Length - 1);
        Assert.Contains(">3.00<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Sample_BuildsTheShapeTheReportBinds()
    {
        var definition = ReadInvoice();
        var sample = ReportBindings.Sample(definition);
        var json = sample.GetRawText();

        Assert.Contains("\"invoice\"", json, StringComparison.Ordinal);
        Assert.Contains("\"number\"", json, StringComparison.Ordinal);
        Assert.Contains("\"lines\"", json, StringComparison.Ordinal);
        Assert.Contains("\"description\"", json, StringComparison.Ordinal);
        Assert.Contains("\"amount\"", json, StringComparison.Ordinal);

        var merged = ReportBindings.MergeMissing(ReadInvoiceData(), sample);
        Assert.Equal("INV-1001", merged.GetProperty("invoice").GetProperty("number").GetString());
        Assert.Equal(2, merged.GetProperty("lines").GetArrayLength());
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
