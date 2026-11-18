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
            SchemaVersion = 9,
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
                                        Columns =
                                        [
                                            new Column
                                            {
                                                Blocks =
                                                [
                                                    new ColumnsBlock
                                                    {
                                                        Id = "deeper",
                                                        Columns = [new Column(), new Column()]
                                                    }
                                                ]
                                            },
                                            new Column()
                                        ]
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
        Assert.Contains("class=\"page-chrome page-footer\"", html, StringComparison.Ordinal);
        Assert.Contains("table-header-group", html, StringComparison.Ordinal);
        Assert.Contains("table-footer-group", html, StringComparison.Ordinal);
        Assert.Contains(">Acme<", html, StringComparison.Ordinal);
        Assert.Contains(">File copy<", html, StringComparison.Ordinal);
        Assert.Contains(".page-chrome { overflow: hidden; }", html, StringComparison.Ordinal);
        Assert.DoesNotContain("page-chrome { position: fixed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"page-watermark\"", html, StringComparison.Ordinal);
        Assert.Contains("counter(page)", html, StringComparison.Ordinal);
        Assert.Contains("counter(pages)", html, StringComparison.Ordinal);
        Assert.Contains("margin: 12mm 12mm 18mm 12mm;", html, StringComparison.Ordinal);
        Assert.Contains("style=\"height:12mm\"", html, StringComparison.Ordinal);
        Assert.Contains("style=\"height:10mm\"", html, StringComparison.Ordinal);
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
    public void Table_WidthsApplyWithoutAHeader()
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
                        ShowHeader = false,
                        Columns =
                        [
                            new TableColumn { Header = "Description", Path = "description", WidthMm = 80 },
                            new TableColumn { Header = "Amount", Path = "amount", WidthMm = 30, Align = TextAlign.Right }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new
        {
            lines = new[] { new { description = "Widget", amount = 12 } }
        });

        var html = ReportCompiler.Compile(definition, data);

        Assert.Contains("<colgroup><col style=\"width:80mm\"><col style=\"width:30mm\"></colgroup>", html, StringComparison.Ordinal);
        Assert.Contains("table-layout: fixed", html, StringComparison.Ordinal);
        Assert.Contains("class=\"block layout-fixed\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<th", html, StringComparison.Ordinal);
        Assert.Contains(">Widget<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_GroupedWidthsUseTheColumnGroup()
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
                            new TableColumn { Header = "Customer", Path = "customer", WidthMm = 40 },
                            new TableColumn { Header = "Note", Path = "note", WidthMm = 70 },
                            new TableColumn { Header = "Amount", Path = "amount", WidthMm = 25, Align = TextAlign.Right }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new
        {
            lines = new[]
            {
                new { customer = "Ada", note = "First", amount = 1 },
                new { customer = "Bea", note = "Second", amount = 2 }
            }
        });

        var html = ReportCompiler.Compile(definition, data, new CompileOptions { Culture = CultureInfo.InvariantCulture });

        Assert.Contains("<colgroup><col style=\"width:40mm\"><col style=\"width:70mm\"><col style=\"width:25mm\"></colgroup>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"group layout-fixed\"", html, StringComparison.Ordinal);
        Assert.Contains("<th colspan=\"3\">Ada</th>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<th class=\"left\" style=\"width:", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("<colgroup>", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ImagesAndRuns_BindPerRecordAndStayStructured()
    {
        const string red = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGM4IScHAAK2AQU0pnWqAAAAAElFTkSuQmCC";
        const string blue = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGOQCzgBAAHGATfKMLLEAAAAAElFTkSuQmCC";
        var definition = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new ImageBlock
                    {
                        Id = "logo",
                        Source = red,
                        Alt = "Literal mark",
                        WidthMm = 12,
                        HeightMm = 12,
                        Fit = ImageFit.Contain,
                        Align = TextAlign.Center
                    },
                    new ImageBlock
                    {
                        Id = "secret",
                        SourcePath = "hiddenPhoto",
                        Alt = "Hidden photo",
                        When = new ShowWhen { Path = "showHidden", Kind = ShowWhenKind.NotEmpty }
                    },
                    new RepeatBlock
                    {
                        Id = "cards",
                        DataPath = "cards",
                        Blocks =
                        [
                            new ImageBlock
                            {
                                Id = "photo",
                                SourcePath = "item.photo",
                                Alt = "Record photo",
                                WidthMm = 16,
                                HeightMm = 16,
                                Fit = ImageFit.Contain,
                                Align = TextAlign.Left
                            },
                            new TextBlock
                            {
                                Id = "letter",
                                SpaceBeforeMm = 2,
                                SpaceAfterMm = 3,
                                Runs =
                                [
                                    new TextRun { Text = "Hello " },
                                    new TextRun { Path = "item.name", Bold = true, Italic = true },
                                    new TextRun { Text = " <note>", Underline = true }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new
        {
            hiddenPhoto = blue,
            showHidden = "",
            cards = new object[]
            {
                new { name = "Ada", photo = red },
                new { name = "Bea", photo = blue },
                new { name = "Cy", photo = "" }
            }
        });

        var html = ReportCompiler.Compile(definition, data);
        var again = ReportJson.Read(ReportJson.Write(definition));

        Assert.Contains("alt=\"Literal mark\"", html, StringComparison.Ordinal);
        Assert.Contains("object-fit:contain", html, StringComparison.Ordinal);
        Assert.Contains("margin-left:auto;margin-right:auto", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, red));
        Assert.Equal(1, Count(html, blue));
        Assert.DoesNotContain("Hidden photo", html, StringComparison.Ordinal);
        Assert.Equal(3, Count(html, "<img"));
        Assert.DoesNotContain("src=\"\"", html, StringComparison.Ordinal);
        Assert.Contains("<span>Hello </span>", html, StringComparison.Ordinal);
        Assert.Contains("<span style=\"font-weight:700;font-style:italic;\">Ada</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span style=\"font-weight:700;font-style:italic;\">Bea</span>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;note&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<note>", html, StringComparison.Ordinal);
        Assert.Contains("margin-top:2mm;", html, StringComparison.Ordinal);
        Assert.Contains("margin-bottom:3mm;", html, StringComparison.Ordinal);
        Assert.Equal(ImageFit.Contain, ((ImageBlock)again.Body.Blocks[0]).Fit);
        Assert.Equal("item.photo", ((ImageBlock)((RepeatBlock)again.Body.Blocks[2]).Blocks[0]).SourcePath);
        Assert.Equal(3, ((TextBlock)((RepeatBlock)again.Body.Blocks[2]).Blocks[1]).Runs!.Count);
    }

    [Fact]
    public void Repeat_KeepsDistinctChildrenAndIndependentLists()
    {
        var definition = OrdersReport();
        var data = OrdersData();
        var html = ReportCompiler.Compile(definition, data, new CompileOptions { Culture = CultureInfo.InvariantCulture });
        var again = ReportJson.Read(ReportJson.Write(definition));

        Assert.Equal(6, Count(html, "repeat-item keep"));
        Assert.Contains(">SO-1<", html, StringComparison.Ordinal);
        Assert.Contains(">SO-2<", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, ">SO-1<"));
        Assert.Equal(1, Count(html, ">SO-2<"));
        Assert.Contains(">L-1 Same name<", html, StringComparison.Ordinal);
        Assert.Contains(">L-2 Same name<", html, StringComparison.Ordinal);
        Assert.Contains(">L-3 Same name<", html, StringComparison.Ordinal);
        Assert.Contains(">L-4 Same name<", html, StringComparison.Ordinal);
        Assert.Equal(4, Count(html, "Same name"));
        Assert.Contains("Northwind", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, ">Northwind<"));

        var first = Slice(html, ">L-1 Same name<", ">L-2 Same name<");
        Assert.Contains(">n1<", first, StringComparison.Ordinal);
        Assert.Contains(">n2<", first, StringComparison.Ordinal);
        Assert.DoesNotContain(">n3<", first, StringComparison.Ordinal);
        Assert.Contains(">A<", first, StringComparison.Ordinal);
        Assert.DoesNotContain(">B<", first, StringComparison.Ordinal);
        Assert.Contains(">c1<", first, StringComparison.Ordinal);
        Assert.Contains(">c2<", first, StringComparison.Ordinal);
        Assert.Contains(">c3<", first, StringComparison.Ordinal);
        Assert.DoesNotContain(">c4<", first, StringComparison.Ordinal);
        Assert.Contains(">Ada<", first, StringComparison.Ordinal);
        Assert.Equal(16, Count(html, "<table class=\"block"));
        Assert.Contains("break-before", html, StringComparison.Ordinal);
        Assert.Equal(html, ReportCompiler.Compile(again, data, new CompileOptions { Culture = CultureInfo.InvariantCulture }));

        var keyed = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new TableBlock
                    {
                        Id = "grouped",
                        DataPath = "lines",
                        GroupBy = "label",
                        GroupKey = "id",
                        ShowHeader = false,
                        Columns = [new TableColumn { Header = "Label", Path = "label" }, new TableColumn { Header = "Qty", Path = "qty" }]
                    }
                ]
            }
        };
        var keyedHtml = ReportCompiler.Compile(keyed, JsonSerializer.SerializeToElement(new
        {
            lines = new[]
            {
                new { id = "A-1", label = "Same name", qty = 1 },
                new { id = "A-2", label = "Same name", qty = 4 }
            }
        }), new CompileOptions { Culture = CultureInfo.InvariantCulture });
        Assert.Equal(2, Count(keyedHtml, "<th colspan=\"2\">Same name</th>"));
        Assert.Contains(">1<", keyedHtml, StringComparison.Ordinal);
        Assert.Contains(">4<", keyedHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Columns_UseWidthsWeightsAndIndependentLists()
    {
        var definition = new ReportDefinition
        {
            Page = new PageSetup { Margin = new Margin { Top = 10, Right = 10, Bottom = 10, Left = 10 } },
            Body =
            {
                Blocks =
                [
                    new ColumnsBlock
                    {
                        Id = "quad",
                        GapMm = 4,
                        Columns =
                        [
                            new Column
                            {
                                WidthMm = 98,
                                Blocks =
                                [
                                    new TextBlock { Id = "h1", Text = "COL-1" },
                                    new TableBlock
                                    {
                                        Id = "t1",
                                        DataPath = "notes",
                                        ShowHeader = false,
                                        Columns = [new TableColumn { Header = "Text", Path = "text" }]
                                    }
                                ]
                            },
                            new Column
                            {
                                WidthMm = 39,
                                Blocks =
                                [
                                    new TextBlock { Id = "h2", Text = "COL-2" },
                                    new TableBlock
                                    {
                                        Id = "t2",
                                        DataPath = "parts",
                                        ShowHeader = false,
                                        Columns = [new TableColumn { Header = "Sku", Path = "sku" }]
                                    }
                                ]
                            },
                            new Column
                            {
                                WidthMm = 24,
                                Blocks = [new TextBlock { Id = "h3", Text = "COL-3" }]
                            },
                            new Column
                            {
                                Blocks =
                                [
                                    new TextBlock { Id = "h4", Text = "COL-4" },
                                    new ColumnsBlock
                                    {
                                        Id = "nested",
                                        Columns =
                                        [
                                            new Column { Weight = 2, Blocks = [new TextBlock { Id = "n1", Text = "NEST-A" }] },
                                            new Column { Weight = 1, Blocks = [new TextBlock { Id = "n2", Text = "NEST-B" }] }
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new
        {
            notes = new[] { new { text = "Note one" }, new { text = "Note two" }, new { text = "Note three" } },
            parts = new[] { new { sku = "Part one" } }
        });

        var html = ReportCompiler.Compile(definition, data);
        var again = ReportJson.Read(ReportJson.Write(definition));

        Assert.Contains("grid-template-columns:98mm 39mm 24mm minmax(0,1fr);align-items:start;gap:4mm;", html, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:2fr 1fr;align-items:start;", html, StringComparison.Ordinal);
        Assert.Contains("COL-1", html, StringComparison.Ordinal);
        Assert.Contains("Note three", html, StringComparison.Ordinal);
        Assert.Contains("Part one", html, StringComparison.Ordinal);
        Assert.Equal(3, html.Split(">Note ", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split(">Part ", StringSplitOptions.None).Length - 1);
        Assert.Equal(98m, ((ColumnsBlock)again.Body.Blocks[0]).Columns[0].WidthMm);
        Assert.Null(((ColumnsBlock)again.Body.Blocks[0]).Columns[3].WidthMm);
        Assert.Empty(ReportValidator.Validate(definition));

        var tooDeep = ReportValidator.Validate(new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new ColumnsBlock
                    {
                        Id = "a",
                        Columns =
                        [
                            new Column
                            {
                                Blocks =
                                [
                                    new ColumnsBlock
                                    {
                                        Id = "b",
                                        Columns =
                                        [
                                            new Column
                                            {
                                                Blocks =
                                                [
                                                    new ColumnsBlock { Id = "c", Columns = [new Column(), new Column()] }
                                                ]
                                            },
                                            new Column()
                                        ]
                                    }
                                ]
                            },
                            new Column()
                        ]
                    }
                ]
            }
        });
        Assert.Contains(tooDeep, error => error.Contains("another columns block", StringComparison.Ordinal));
    }

    [Fact]
    public void Styles_RoundTripAndPrintWithoutHostCss()
    {
        var definition = new ReportDefinition
        {
            Name = "Letter",
            Body =
            {
                Blocks =
                [
                    new TextBlock
                    {
                        Id = "greeting",
                        Text = "Dear customer,",
                        FontFamily = "Times New Roman",
                        FontSizePt = 12,
                        Italic = true,
                        Underline = true,
                        LineHeight = 1.35m,
                        Style = new BoxStyle
                        {
                            Margin = new Spacing { Top = 1, Bottom = 3 },
                            Padding = new Spacing { Top = 1, Right = 2, Bottom = 1, Left = 2 }
                        }
                    },
                    new RuleBlock { Id = "rule", ThicknessMm = 0.4m, Color = "#111827", MarginTopMm = 2, MarginBottomMm = 2 },
                    new PanelBlock
                    {
                        Id = "notes",
                        Style = new BoxStyle
                        {
                            Background = "#f8fafc",
                            Padding = new Spacing { Top = 3, Right = 3, Bottom = 3, Left = 3 },
                            Border = new BorderLine { WidthMm = 0.3m, Color = "#1f2937" }
                        },
                        Blocks =
                        [
                            new TextBlock { Id = "note", Text = "Packed with care.", FontFamily = "Segoe UI" }
                        ]
                    },
                    new PanelBlock
                    {
                        Id = "terms",
                        Style = new BoxStyle
                        {
                            Background = "#ffffff",
                            Border = new BorderLine { WidthMm = 0.2m, Color = "#9ca3af", Top = true, Bottom = true }
                        },
                        Blocks = [new TextBlock { Id = "term", Text = "Payable in 30 days." }]
                    },
                    new TableBlock
                    {
                        Id = "lines",
                        DataPath = "lines",
                        TableStyle = new TableStyle
                        {
                            FontFamily = "Arial",
                            FontSizePt = 10,
                            Italic = true,
                            CellPaddingMm = 2,
                            BorderWidthMm = 0.2m,
                            BorderColor = "#111827",
                            HeaderBackground = "#e5e7eb"
                        },
                        Columns =
                        [
                            new TableColumn { Header = "Item", Path = "item" },
                            new TableColumn { Header = "Qty", Path = "qty" }
                        ]
                    }
                ]
            }
        };
        var data = JsonSerializer.SerializeToElement(new { lines = new[] { new { item = "Lamp", qty = 1 } } });
        var saved = ReportJson.Write(definition);
        var again = ReportJson.Read(saved);
        var html = ReportCompiler.Compile(again, data);

        Assert.Equal(ReportDefinition.CurrentSchemaVersion, again.SchemaVersion);
        Assert.Equal("Times New Roman", ((TextBlock)again.Body.Blocks[0]).FontFamily);
        Assert.True(((TextBlock)again.Body.Blocks[0]).Italic);
        Assert.True(((TextBlock)again.Body.Blocks[0]).Underline);
        Assert.Equal(1.35m, ((TextBlock)again.Body.Blocks[0]).LineHeight);
        Assert.Equal(0.4m, ((RuleBlock)again.Body.Blocks[1]).ThicknessMm);
        Assert.Equal("#f8fafc", ((PanelBlock)again.Body.Blocks[2]).Style!.Background);
        Assert.Equal("#ffffff", ((PanelBlock)again.Body.Blocks[3]).Style!.Background);
        Assert.True(((PanelBlock)again.Body.Blocks[3]).Style!.Border!.Top);
        Assert.False(((PanelBlock)again.Body.Blocks[3]).Style!.Border!.Left);
        Assert.Equal("Arial", ((TableBlock)again.Body.Blocks[4]).TableStyle!.FontFamily);
        Assert.Contains("font-family:&quot;Times New Roman&quot;, &quot;Source Sans 3&quot;, serif", html, StringComparison.Ordinal);
        Assert.Contains("font-style:italic", html, StringComparison.Ordinal);
        Assert.Contains("text-decoration:underline", html, StringComparison.Ordinal);
        Assert.Contains("line-height:1.35", html, StringComparison.Ordinal);
        Assert.Contains("border-top:0.4mm solid #111827", html, StringComparison.Ordinal);
        Assert.Contains("background:#f8fafc", html, StringComparison.Ordinal);
        Assert.Contains("border:0.3mm solid #1f2937", html, StringComparison.Ordinal);
        Assert.Contains("border-top:0.2mm solid #9ca3af", html, StringComparison.Ordinal);
        Assert.Contains("border-bottom:0.2mm solid #9ca3af", html, StringComparison.Ordinal);
        Assert.Contains("font-family:Arial, &quot;Source Sans 3&quot;, sans-serif", html, StringComparison.Ordinal);
        Assert.Contains("padding:2mm", html, StringComparison.Ordinal);
        Assert.Contains("border:0.2mm solid #111827", html, StringComparison.Ordinal);
        Assert.Contains("background:#e5e7eb", html, StringComparison.Ordinal);
        Assert.Contains("Packed with care.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<style src=", html, StringComparison.Ordinal);
        Assert.Equal(html, ReportCompiler.Compile(definition, data));
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

    private static int Count(string text, string token) => text.Split(token).Length - 1;

    private static string Slice(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        return text[from..to];
    }

    private static ReportDefinition OrdersReport() => new()
    {
        Name = "Orders",
        Body =
        {
            Blocks =
            [
                new RepeatBlock
                {
                    Id = "orders",
                    DataPath = "orders",
                    KeepItemTogether = true,
                    Blocks =
                    [
                        new TextBlock { Id = "order", Text = "{item.id}" },
                        new TextBlock { Id = "company", Text = "{root.company}" },
                        new RepeatBlock
                        {
                            Id = "lines",
                            DataPath = "item.lines",
                            KeepItemTogether = true,
                            BreakBeforeItem = true,
                            Blocks =
                            [
                                new TextBlock { Id = "line", Text = "{item.id} {item.title}" },
                                new TextBlock { Id = "buyer", Text = "{parent.customer}" },
                                new ColumnsBlock
                                {
                                    Id = "lists",
                                    GapMm = 4,
                                    Columns =
                                    [
                                        new Column { WidthMm = 40, Blocks = [Table("notes", "item.notes", "text")] },
                                        new Column { WidthMm = 30, Blocks = [Table("parts", "item.parts", "sku")] },
                                        new Column { WidthMm = 30, Blocks = [Table("tasks", "item.tasks", "name")] },
                                        new Column { Blocks = [Table("charges", "item.charges", "name")] }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        }
    };

    private static TableBlock Table(string id, string path, string column) => new()
    {
        Id = id,
        DataPath = path,
        ShowHeader = false,
        Columns = [new TableColumn { Header = column, Path = column }]
    };

    private static JsonElement OrdersData() => JsonSerializer.SerializeToElement(new
    {
        company = "Northwind",
        orders = new object[]
        {
            new
            {
                id = "SO-1",
                customer = "Ada",
                lines = new object[]
                {
                    new
                    {
                        id = "L-1",
                        title = "Same name",
                        notes = new[] { new { text = "n1" }, new { text = "n2" } },
                        parts = new[] { new { sku = "A" } },
                        tasks = Array.Empty<object>(),
                        charges = new[] { new { name = "c1" }, new { name = "c2" }, new { name = "c3" } }
                    },
                    new
                    {
                        id = "L-2",
                        title = "Same name",
                        notes = new[] { new { text = "n3" } },
                        parts = new[] { new { sku = "B" }, new { sku = "C" } },
                        tasks = new[] { new { name = "t1" }, new { name = "t2" } },
                        charges = new[] { new { name = "c4" } }
                    }
                }
            },
            new
            {
                id = "SO-2",
                customer = "Bea",
                lines = new object[]
                {
                    new
                    {
                        id = "L-3",
                        title = "Same name",
                        notes = new[] { new { text = "n4" } },
                        parts = new[] { new { sku = "D" } },
                        tasks = new[] { new { name = "t3" } },
                        charges = new[] { new { name = "c5" } }
                    },
                    new
                    {
                        id = "L-4",
                        title = "Same name",
                        notes = new[] { new { text = "n5" }, new { text = "n6" } },
                        parts = Array.Empty<object>(),
                        tasks = new[] { new { name = "t4" } },
                        charges = new[] { new { name = "c6" }, new { name = "c7" } }
                    }
                }
            }
        }
    });

    [Fact]
    public void OrdersSample_CompilesTheNestedLayout()
    {
        var definition = ReportJson.Read(File.ReadAllText(Fixture("orders", "definition.json")));
        var data = ReportJson.ReadData(File.ReadAllText(Fixture("orders", "data.json")));
        var html = ReportCompiler.Compile(definition, data, new CompileOptions
        {
            Culture = Us,
            Clock = new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero)
        });

        Assert.Contains("SO-100", html, StringComparison.Ordinal);
        Assert.Contains("SO-200", html, StringComparison.Ordinal);
        Assert.Contains("Standard kit", html, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:70mm 32mm 28mm minmax(0,1fr)", html, StringComparison.Ordinal);
        Assert.Contains("font-family:&quot;Times New Roman&quot;", html, StringComparison.Ordinal);
        Assert.Contains("Prepared for ", html, StringComparison.Ordinal);
        Assert.Contains("Order SO-100", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "Order SO-100"));
        Assert.Contains("Pack the lamp first.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    private static string CompileInvoice() =>
        ReportCompiler.Compile(ReadInvoice(), ReadInvoiceData(), new CompileOptions { Culture = Us });

    private static ReportDefinition ReadInvoice() =>
        ReportJson.Read(File.ReadAllText(Fixture("definition.json")));

    private static JsonElement ReadInvoiceData() =>
        ReportJson.ReadData(File.ReadAllText(Fixture("data.json")));

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "invoice", name);

    private static string Fixture(string folder, string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", folder, name);
}
