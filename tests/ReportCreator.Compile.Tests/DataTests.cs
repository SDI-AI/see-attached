using System.Globalization;
using System.Text.Json;
using ReportCreator.Compile;
using ReportCreator.Model;

namespace ReportCreator.Compile.Tests;

public class DataTests
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void FromValues_InfersTypesAndRoundTrips()
    {
        var plain = ReadInvoiceData();
        var document = DataJson.FromValues(plain, "Invoice");

        var invoice = document.Fields.Single(field => field.Name == "invoice");
        Assert.Equal(DataFieldType.Object, invoice.Type);
        Assert.Equal(DataFieldType.String, invoice.Fields.Single(field => field.Name == "number").Type);
        Assert.Equal(DataFieldType.Date, invoice.Fields.Single(field => field.Name == "date").Type);
        var lines = document.Fields.Single(field => field.Name == "lines");
        Assert.Equal(DataFieldType.Array, lines.Type);
        Assert.Equal(DataFieldType.String, lines.Fields.Single(field => field.Name == "description").Type);
        Assert.Equal(DataFieldType.Number, lines.Fields.Single(field => field.Name == "amount").Type);

        var again = DataJson.Read(DataJson.Write(document));
        Assert.Equal("data", again.Kind);
        Assert.Equal(DataFieldType.Date, again.Fields.Single(field => field.Name == "invoice").Fields.Single(field => field.Name == "date").Type);
        Assert.Equal(Canonical(plain), Canonical(again.Values));
        Assert.Empty(DataValidator.Validate(again));
    }

    [Fact]
    public void Values_FeedMoreThanOneReport()
    {
        var document = DataJson.FromValues(ReadInvoiceData(), "Invoice");
        var invoice = ReportJson.Read(File.ReadAllText(Fixture("definition.json")));
        var fromFile = ReportCompiler.Compile(invoice, ReadInvoiceData(), new CompileOptions { Culture = Us });
        var fromDocument = ReportCompiler.Compile(invoice, document.Values, new CompileOptions { Culture = Us });
        Assert.Equal(fromFile, fromDocument);

        var other = new ReportDefinition
        {
            Body = { Blocks = [new TextBlock { Id = "n", Text = "No. {invoice.number}" }] }
        };
        var html = ReportCompiler.Compile(other, DataJson.ReadValues(DataJson.Write(document)));
        Assert.Contains("INV-1001", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_ChangesTypeAddsARowAndKeepsTheShape()
    {
        var document = DataJson.FromValues(ReadInvoiceData(), "Invoice");
        DataEdit.SetType(document, "lines.amount", DataFieldType.String);
        Assert.Equal("25", document.Values.GetProperty("lines")[0].GetProperty("amount").GetString());

        DataEdit.AddRow(document, "lines");
        Assert.Equal(3, document.Values.GetProperty("lines").GetArrayLength());
        Assert.Equal("", document.Values.GetProperty("lines")[2].GetProperty("description").GetString());
        Assert.Equal(0, document.Values.GetProperty("lines")[2].GetProperty("qty").GetDecimal());

        DataEdit.SetCell(document, "lines", 2, "description", "Cable");
        Assert.Equal("Cable", document.Values.GetProperty("lines")[2].GetProperty("description").GetString());

        var errors = DataValidator.Validate(document);
        Assert.Empty(errors);

        document.Values = JsonSerializer.SerializeToElement(new { invoice = new { date = "March" } });
        var broken = DataValidator.Validate(document);
        Assert.Contains(broken, error => error.Contains("invoice.date", StringComparison.Ordinal) && error.Contains("yyyy-MM-dd", StringComparison.Ordinal));
    }

    [Fact]
    public void Edit_NestsACollectionUnderAnObject()
    {
        var document = new DataDocument { Name = "Invoice", Values = JsonSerializer.SerializeToElement(new { }) };
        DataEdit.Add(document, null, "invoice", DataFieldType.Object);
        DataEdit.Add(document, "invoice", "number", DataFieldType.String);
        DataEdit.SetValue(document, "invoice.number", "INV-1001");
        DataEdit.Add(document, "invoice", "items", DataFieldType.Array);
        DataEdit.Add(document, "invoice.items", "description", DataFieldType.String);
        DataEdit.Add(document, "invoice.items", "amount", DataFieldType.Number);
        DataEdit.AddRow(document, "invoice.items");
        DataEdit.SetCell(document, "invoice.items", 0, "description", "Widget");
        DataEdit.SetCell(document, "invoice.items", 0, "amount", "25");

        DataEdit.Add(document, "invoice.items", "product", DataFieldType.Object);
        DataEdit.Add(document, "invoice.items.product", "sku", DataFieldType.String);
        DataEdit.Add(document, "invoice.items", "taxes", DataFieldType.Array);
        DataEdit.Add(document, "invoice.items.taxes", "name", DataFieldType.String);
        DataEdit.AddRow(document, "invoice.items.taxes");
        DataEdit.SetCell(document, "invoice.items.taxes", 0, "name", "VAT");

        var invoice = document.Values.GetProperty("invoice");
        Assert.Equal("INV-1001", invoice.GetProperty("number").GetString());
        var item = invoice.GetProperty("items")[0];
        Assert.Equal("Widget", item.GetProperty("description").GetString());
        Assert.Equal(25m, item.GetProperty("amount").GetDecimal());
        Assert.Equal("", item.GetProperty("product").GetProperty("sku").GetString());
        Assert.Equal("VAT", item.GetProperty("taxes")[0].GetProperty("name").GetString());
        Assert.Equal(DataFieldType.Array, DataEdit.Find(document, "invoice.items")!.Type);
        Assert.Equal(DataFieldType.String, DataEdit.Find(document, "invoice.items.taxes.name")!.Type);
        Assert.Empty(DataValidator.Validate(document));
    }

    [Fact]
    public void Include_AddsTheReportShapeWithoutReplacingValues()
    {
        var document = DataJson.FromValues(JsonSerializer.SerializeToElement(new { invoice = new { number = "INV-9" } }), "Shared");
        var definition = new ReportDefinition
        {
            Body =
            {
                Blocks =
                [
                    new TextBlock { Id = "when", Text = "{invoice.date:yyyy-MM-dd}" },
                    new TableBlock
                    {
                        Id = "lines",
                        DataPath = "lines",
                        Columns = [new TableColumn { Header = "Amount", Path = "amount", Format = "0.00" }]
                    }
                ]
            }
        };

        ReportBindings.Include(document, definition);

        Assert.Equal("INV-9", document.Values.GetProperty("invoice").GetProperty("number").GetString());
        Assert.Equal(DataFieldType.Date, DataEdit.Find(document, "invoice.date")!.Type);
        Assert.Equal("2026-01-01", document.Values.GetProperty("invoice").GetProperty("date").GetString());
        Assert.Equal(DataFieldType.Array, DataEdit.Find(document, "lines")!.Type);
        Assert.Equal(DataFieldType.Number, DataEdit.Find(document, "lines.amount")!.Type);
        Assert.Equal(0, document.Values.GetProperty("lines")[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public void ReadAny_AcceptsAPlainObjectAndRejectsAReport()
    {
        var values = DataJson.ReadValues(File.ReadAllText(Fixture("data.json")));
        Assert.Equal("INV-1001", values.GetProperty("invoice").GetProperty("number").GetString());

        var report = File.ReadAllText(Fixture("definition.json"));
        var error = Assert.Throws<JsonException>(() => DataJson.ReadAny(report));
        Assert.Contains("report layout", error.Message, StringComparison.Ordinal);
    }

    private static string Canonical(JsonElement element) =>
        JsonSerializer.Serialize(element, ReportJson.Options);

    private static JsonElement ReadInvoiceData() =>
        ReportJson.ReadData(File.ReadAllText(Fixture("data.json")));

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "invoice", name);
}
