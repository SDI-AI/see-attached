# Setup and creating reports

See Attached builds the report that rides along as an attachment. The definition is the layout, saved as JSON. The data is a separate JSON object. .NET binds them and compiles one HTML file. A browser shows that HTML. Playwright prints the same HTML to PDF.

The invoice files in `fixtures/invoice` are a complete example. `definition.json` is the layout. `data.json` is the object the layout binds.

## Requirements

- .NET 10 SDK. `global.json` asks for SDK `10.0.302` or a later 10.x release.
- Windows, for the WPF builder. The WebView2 runtime must be installed.
- Node.js, only if you run the Next.js sample.
- Chromium, only if you export PDF. The steps below install it.

## Get the code

```powershell
git clone https://github.com/SDI-AI/see-attached
Set-Location see-attached
dotnet build ReportCreator.slnx
```

Compiler tests do not need a browser:

```powershell
dotnet test --project tests/ReportCreator.Compile.Tests
```

Run `dotnet test` with no extra flags. This repo uses Microsoft.Testing.Platform, and flags such as `--nologo` are rejected.

## Install Chromium for PDF

Build an app that references `ReportCreator.Pdf` first. The class library does not copy `Microsoft.Playwright.dll` next to `playwright.ps1`, so run the script from a program output folder:

```powershell
dotnet build tests/ReportCreator.Pdf.Tests
pwsh tests/ReportCreator.Pdf.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Use the same command from `samples/viewer-blazor/bin/Debug/net10.0` or `src/ReportCreator.Builder/bin/Debug/net10.0-windows` after you have built that app, if you export PDF from there.

## Create a report in the builder

```powershell
dotnet run --project src/ReportCreator.Builder
```

The window has the block list on the left, the PDF preview in the middle, and the property panel on the right. The preview is the same PDF export the File menu writes. It refreshes about half a second after you change the report or the data. Choosing another block does not reprint.

1. Choose **File → New**, or **File → New invoice sample** to open the invoice with its sample data.
2. Choose **Page setup** and set the paper, orientation, and margins. Lengths are millimetres. Page header and page footer heights are millimetres too. `0` means 14 mm when that band has blocks, and the number strip is 6 mm when page numbers are on. Page numbers are none, bottom left, bottom center, or bottom right, and they print as `Page 1 of 2`. Uncheck **Number the first page** to leave that number off page 1. The page header still prints there.
3. In the block list, pick Header, Body, Footer, Page header, or Page footer, pick a block kind, and click **Add**.
4. Select the block and edit it in the property panel.
5. Use **Up**, **Down**, and **Delete** to reorder the list.
6. Open the **Data** expander to edit the preview JSON. **Insert missing fields** adds paths the report binds and leaves values you already typed. Choose **File → Open data** and pick a JSON object such as `fixtures/invoice/data.json`. That data is for the preview. Save does not write it into the report file.
7. Choose **File → Save as**. A `.json` file is the definition. A `.rcreport` file is a zip that contains `definition.json`.
8. Choose **File → Export HTML** or **File → Export PDF**.

Header, body, and footer each print once. Page header and page footer repeat on every page. Put the letterhead in the page header and a one-time title in the report header.

## Parameters and print fields

A parameter is a value the caller passes next to the data. Declare it on the report. `kind` is `string`, `number`, `date`, or `bool`. `default` is optional. A missing parameter uses that default. A blank value does not.

```json
"parameters": [
  { "name": "copies", "kind": "number", "default": "1" }
]
```

In text, write `{param.copies}` or `{param.copies:0}`. A field path can be `param.copies`. `{param.customer.name}` is still a data path, because a parameter name has no dot.

Three print fields are not data, and they are not stored on the sample JSON: `{report.name}`, `{print.date}`, and `{print.time}`. A format uses the same .NET date patterns as a field, for example `{print.date:MMMM d, yyyy}`. Pass `CompileOptions.Clock` when the printed time must stay fixed. Page numbers are not tokens. They stay in the page margin.

```csharp
using System.Globalization;
using System.Text.Json;
using ReportCreator.Compile;

var html = ReportCompiler.Compile(definition, data, new CompileOptions
{
    Culture = CultureInfo.GetCultureInfo("en-US"),
    Clock = new DateTimeOffset(2026, 3, 14, 15, 4, 0, TimeSpan.Zero),
    Parameters = JsonSerializer.SerializeToElement(new { copies = 2 })
});
```

`PdfExportOptions` takes the same `Parameters` object and `Clock` when you export PDF. There is no prompt form. The caller asks the user.

## See the invoice

Blazor compiles the fixture, then shows zoom, print, and Make PDF at `http://localhost:5250`:

```powershell
dotnet run --project samples/viewer-blazor --launch-profile http
```

The Next.js sample only displays HTML. Compile the fixture, then start the page from `samples/viewer-next`:

```powershell
dotnet run --project samples/render-fixture
Set-Location samples/viewer-next
npm install
npm run dev
```

Run `npm` inside `samples/viewer-next`. Do not add a `package.json` at the repository root.

## Blocks

Every block needs its own id: letters, digits, `_`, or `-`, up to 64 characters. **Keep together** asks the printer not to split that block. A missing data path prints nothing. Values are HTML-encoded. Formats are .NET number or date formats, and the culture is chosen when you compile.

| Block | Use it for |
| --- | --- |
| Text | Words, plus `{path}` or `{path:format}` tokens. A newline in the text is a line break. |
| Field | One value, such as `invoice.billTo.name`. Same font, bold, color, and alignment as text. |
| Image | An `https://` URL, or a PNG, JPEG, GIF, or WebP data URI stored in the JSON. Width and height are millimetres. |
| Spacer | Empty vertical room, in millimetres. |
| Columns | Two or three equal columns. The builder adds text, field, and spacer inside a column. A columns block cannot contain another columns block. |
| Table | Rows from an array of objects. Column paths are read from each row. Sum, Count, and Avg print in a footer. The header row repeats when the table continues on the next page. **Group by** is a path on each row. **Group totals** print under each group. **Running total** replaces that column with the sum so far and starts again at each group. |

A text block mixes words and values:

```json
{ "type": "text", "id": "meta", "align": "right",
  "text": "No. {invoice.number}\n{invoice.date:MMMM d, yyyy}" }
```

With culture `en-US`, `{invoice.date:MMMM d, yyyy}` prints `March 14, 2026`. A field is the whole value:

```json
{ "type": "field", "id": "customer", "path": "invoice.billTo.name", "bold": true }
```

A table's `dataPath` points at the array. Each column path is relative to one row:

```json
{
  "type": "table",
  "id": "lines",
  "dataPath": "lines",
  "columns": [
    { "header": "Description", "path": "description" },
    { "header": "Amount", "path": "amount", "format": "C", "align": "right" }
  ],
  "aggregates": [
    { "label": "Total", "columnPath": "amount", "kind": "sum", "format": "C" }
  ]
}
```

The matching data is a JSON object. `C` on `en-US` prints currency, and the footer prints `Total` with the sum:

```json
{
  "invoice": {
    "number": "INV-1001",
    "date": "2026-03-14",
    "billTo": { "name": "Northwind Traders", "city": "Seattle" }
  },
  "lines": [
    { "description": "Widget", "amount": 25 },
    { "description": "Gadget", "amount": 40 }
  ]
}
```

An image is part of the layout, not the data object:

```json
{
  "type": "image",
  "id": "logo",
  "alt": "Logo",
  "widthMm": 40,
  "source": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="
}
```

The whole `source` value can be up to 2,000,000 characters. Font size is 6 to 96 points. Color is `#RGB` or `#RRGGBB`.

Two aggregates on the same column show only the first one. The aggregate label prints in the first column when that column has no aggregate of its own.

**Show when** hides a block from one path. Leave the path blank to always show it. The kinds are not empty, empty, equals, not equals, greater than, and less than. A comparison needs a value. Numbers compare as decimals. Everything else compares as text. A missing path counts as empty.

```json
{ "type": "text", "id": "paid", "text": "Paid", "when": { "path": "invoice.balance", "kind": "equals", "value": "0" } }
```

A grouped table prints a heading for each distinct value, in the order it first appears. An empty key prints `(blank)`.

```json
{
  "type": "table",
  "id": "lines",
  "dataPath": "lines",
  "groupBy": "customer",
  "columns": [
    { "header": "Customer", "path": "customer" },
    { "header": "Amount", "path": "amount", "format": "0.00", "align": "right", "runningTotal": true }
  ],
  "groupAggregates": [
    { "label": "Subtotal", "columnPath": "amount", "kind": "sum", "format": "0.00" }
  ],
  "aggregates": [
    { "label": "Total", "columnPath": "amount", "kind": "sum", "format": "0.00" }
  ]
}
```

## Code samples

Reference `ReportCreator.Compile` to load a report and produce HTML. Reference `ReportCreator.Pdf` as well when you export PDF. Reference `ReportCreator.Blazor` for the viewer component.

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\ReportCreator.Compile\ReportCreator.Compile.csproj" />
  <ProjectReference Include="..\..\src\ReportCreator.Pdf\ReportCreator.Pdf.csproj" />
</ItemGroup>
```

### Load files and compile

```csharp
using System.Globalization;
using ReportCreator.Compile;
using ReportCreator.Model;

var definition = ReportJson.Read(File.ReadAllText("invoice.definition.json"));
var data = ReportJson.ReadData(File.ReadAllText("invoice.data.json"));

var html = ReportCompiler.Compile(definition, data, new CompileOptions
{
    Culture = CultureInfo.GetCultureInfo("en-US")
});

File.WriteAllText("invoice.html", html);
```

`ReadData` requires a JSON object. The calling code does not read the page layout. It does supply the paths that the definition binds, such as `invoice.number` and `lines`.

### Build the definition in C#

`ReportJson.Write` saves the same JSON the builder saves.

```csharp
using ReportCreator.Model;

var definition = new ReportDefinition
{
    Name = "Invoice",
    Page = new PageSetup
    {
        Paper = PaperKind.A4,
        Orientation = PageOrientation.Portrait,
        Margin = new Margin { Top = 12, Right = 12, Bottom = 12, Left = 12 },
        PageHeaderMm = 12,
        PageNumbers = PageNumberPlacement.BottomCenter
    },
    PageHeader =
    {
        Blocks =
        [
            new TextBlock { Id = "running", Text = "Acme" }
        ]
    },
    Header =
    {
        Blocks =
        [
            new TextBlock { Id = "title", Text = "Invoice", Bold = true, FontSizePt = 28 }
        ]
    },
    Body =
    {
        Blocks =
        [
            new TextBlock
            {
                Id = "meta",
                Align = TextAlign.Right,
                Text = "No. {invoice.number}\n{invoice.date:MMMM d, yyyy}"
            },
            new TableBlock
            {
                Id = "lines",
                DataPath = "lines",
                Columns =
                [
                    new TableColumn { Header = "Description", Path = "description" },
                    new TableColumn
                    {
                        Header = "Amount",
                        Path = "amount",
                        Format = "C",
                        Align = TextAlign.Right
                    }
                ],
                Aggregates =
                [
                    new TableAggregate
                    {
                        Label = "Total",
                        ColumnPath = "amount",
                        Kind = AggregateKind.Sum,
                        Format = "C"
                    }
                ]
            }
        ]
    }
};

File.WriteAllText("invoice.definition.json", ReportJson.Write(definition));
```

### Data built in code

Pass `ReportJson.Options` so the property names are camel case and match the paths.

```csharp
using System.Text.Json;

var data = JsonSerializer.SerializeToElement(new
{
    Invoice = new
    {
        Number = "INV-1001",
        Date = "2026-03-14",
        BillTo = new { Name = "Northwind Traders", City = "Seattle" }
    },
    Lines = new[]
    {
        new { Description = "Widget", Amount = 25m },
        new { Description = "Gadget", Amount = 40m }
    }
}, ReportJson.Options);
```

### PDF

Keep one browser open and pass the same definition and data. The exporter compiles the HTML itself. Install Chromium first.

```csharp
using ReportCreator.Pdf;

await using var exporter = await ReportPdfExporter.LaunchAsync(new PdfExportOptions
{
    Culture = CultureInfo.GetCultureInfo("en-US")
});

var pdf = await exporter.ExportAsync(definition, data);
await File.WriteAllBytesAsync("invoice.pdf", pdf);
```

Set `Container = true` when the process runs in Azure Container Apps. Leave it off on Windows. The [README](../README.md) has the container notes.

The repository `Dockerfile` publishes the Blazor sample and installs Chromium into `mcr.microsoft.com/dotnet/aspnet:10.0`. Docker must be using Linux containers.

```powershell
docker build -t see-attached .
docker run --rm -p 8080:8080 see-attached
```

Open `http://localhost:8080`. The image sets `REPORTCREATOR_CONTAINER=1`. The sample reads that variable and turns on `Container`. The library does not read it. Give the container at least 1 GB of memory.

### Show the HTML in Blazor

Add the Blazor project reference and the compiled HTML. The component does not bind data. Pass `Pdf` when you want the Download PDF button.

```razor
<ReportViewer Html="@html" Pdf="@pdf" PdfFileName="invoice.pdf" />
```

### Read a .rcreport package

The package is a zip with one entry, `definition.json`. The data stays a separate JSON object.

```csharp
using System.IO.Compression;

using var zip = ZipFile.OpenRead("invoice.rcreport");
var entry = zip.GetEntry("definition.json")
    ?? throw new InvalidDataException("The package has no definition.json.");
using var reader = new StreamReader(entry.Open());
var definition = ReportJson.Read(reader.ReadToEnd());
```

## Where page breaks appear

The builder preview is the PDF, so page breaks, the repeated page header, the repeated page footer, and the page numbers are the printed page. The Blazor and Next.js viewers show one paper-width column. Chromium inserts page breaks when that HTML is printed. A table header repeats on the next page. A row, and a block marked keep together, stay on one page.
