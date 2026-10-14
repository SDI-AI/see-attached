# See Attached

https://seeattachedreports.com

Reports are the attachment. The app gets the attention, and the invoice, the statement, and the list wait until someone asks for the PDF. See Attached is an open-source builder for those documents. A report is a header, a body that flows, and a footer. A page header and a page footer can repeat, with page numbers. Tables split by row. It is not a free canvas and not a pixel-perfect form.

The file you save is JSON. HTML is only the render artifact. .NET binds the data and compiles one self-contained HTML file. A browser shows that file. Playwright prints the same HTML to PDF.

```
definition.json + data.json
        │
        ▼
ReportCreator.Compile (.NET 10)
        │
        ├─ WPF WebView2 preview
        ├─ Blazor iframe
        ├─ Next.js iframe
        └─ Playwright PDF
```

Next.js does not bind data. PDF export compiles from the definition and the data on the server. It does not accept HTML from the browser.

Copyright (c) 2026 Innov8now LLC. License: MIT.

Setup, the WPF builder, and code samples are in [docs/getting-started.md](docs/getting-started.md).

## What v1 does

- Paper sizes A3, A4, A5, Letter, Legal, Tabloid, and custom. Portrait or landscape. Lengths are millimetres, with at most two decimal places.
- Bands: report header, body, and report footer, each once. Page header and page footer repeat on every page. Page numbers print as "Page N of M" at the bottom left, center, or right.
- Blocks: text, bound field, image, spacer, a row of two or three columns, and a table bound to an array. A block can show or hide from a data path. A table can group rows and carry a running total.
- Bindings are property paths (`customer.name`) and .NET numeric or date format strings. The culture is chosen when you compile. Table aggregates are Sum, Count, and Avg, on the group or on the whole table. Values are HTML-encoded.
- Images are `https` URLs or `data:image` PNG, JPEG, GIF, or WebP.
- Print CSS sets `@page` size and margin, repeats a table header with `thead { display: table-header-group }`, and keeps a table row or a `keepTogether` block from splitting.
- The WPF builder previews the real PDF. The Blazor and Next.js viewers show one paper-width column. Chromium inserts page breaks when that HTML is printed.

Not in this version: charts, barcodes, rich text, expressions, right-to-left layout, and a free canvas.

## What 1.1 adds

- Parameters are string, number, date, or bool, each with an optional default. Pass a JSON object of values beside the data. A token `{param.name}` uses the same format rules as a field. A missing parameter uses its default. A blank value stays blank.
- Print fields `{report.name}`, `{print.date}`, and `{print.time}` are not data paths. Page numbers stay in the page margin. They are not tokens in flowing text.
- Page setup can leave the page number off the first page. The repeating page header still prints on that page.

## What 1.2 adds

- A block can break the page before itself or after itself.
- A grouped table is one table per group. The group title and the column header both sit in the table head, so they repeat when the group crosses a page. The report total stays on the last group.
- A later group can start on a new page. A group stays on one page when it fits.

## What 1.21 adds

- A data file is its own typed JSON document, saved as `.rcdata`. It is not stored inside the report, and any report can compile its values.
- Fields are string, number, date, bool, object, or array. An array is a list of rows. A date is `yyyy-MM-dd` or empty. A number is a JSON number.
- The data file holds a sample schema and sample values together. Edit data opens one window. The left side is a tree of nodes. A plus on the root, an object, or a 1:N adds a field under that node. A collection such as `invoice.items` is a 1:N nested under the invoice object. The right side edits the selected node: name, type, and, for an object, relationship One or 1:N. A leaf that is not inside a 1:N has one sample value. A 1:N node edits the sample items. Open still accepts a plain JSON object of values. Insert missing fields adds paths from the open report and keeps values already set.

## Projects

| Project | Role |
| --- | --- |
| `src/ReportCreator.Model` | Papers, validation, JSON model, typed data documents |
| `src/ReportCreator.Compile` | Definition plus data to HTML |
| `src/ReportCreator.Pdf` | Playwright PDF export |
| `src/ReportCreator.Blazor` | Iframe viewer component |
| `src/ReportCreator.Builder` | WPF builder |
| `samples/viewer-blazor` | Blazor host for the invoice fixture |
| `samples/viewer-next` | Next.js host for the same HTML |
| `samples/render-fixture` | Writes `samples/viewer-next/public/invoice.html` |
| `fixtures/invoice` | Invoice definition and data |
| `tests/ReportCreator.Compile.Tests` | HTML checks, no browser |
| `tests/ReportCreator.Pdf.Tests` | Page size, text, and a table that crosses pages |
| `Dockerfile` | Blazor sample image with Chromium installed |

Libraries target `net10.0`. The builder targets `net10.0-windows`. `global.json` pins the .NET 10 SDK.

## Build

```powershell
dotnet build ReportCreator.slnx
dotnet test --project tests/ReportCreator.Compile.Tests
dotnet test --project tests/ReportCreator.Pdf.Tests
```

Run `dotnet test` with no extra flags. This repo uses Microsoft.Testing.Platform, and flags such as `--nologo` are rejected.

PDF export needs Chromium. The class library does not copy `Microsoft.Playwright.dll` next to `playwright.ps1`. Install from the test program's output, which does contain that DLL:

```powershell
pwsh tests/ReportCreator.Pdf.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Use the same command from `samples/viewer-blazor/bin/Debug/net10.0` or `src/ReportCreator.Builder/bin/Debug/net10.0-windows` after you have built that app, if you export PDF from there.

## Viewers

Blazor sample (compiles the invoice, zoom, print, and Make PDF):

```powershell
dotnet run --project samples/viewer-blazor --launch-profile http
```

The page is `http://localhost:5250`. The report iframe is sandboxed with `allow-same-origin` and `allow-modals` only, so report markup cannot run script. Set `REPORTCREATOR_CONTAINER=1` before launch when the sample itself runs in a locked-down container. The library does not read that variable.

Next.js sample (display only, no PDF button):

```powershell
dotnet run --project samples/render-fixture
Set-Location samples/viewer-next
npm install
npm run dev
```

`npm install` and `npm run dev` must run inside `samples/viewer-next`. Do not add a `package.json` at the repository root. Next.js 16 added `@types/node` 26.6.5 as a sample dev dependency the first time `next build` saw TypeScript. It is not used by the .NET libraries.

The WPF builder is `src/ReportCreator.Builder`. File menu: new report, the invoice sample, open and save JSON or a `.rcreport` zip, open and save a `.rcdata` data file, export HTML, and export PDF. Page setup edits paper, orientation, margins, page-header height, page-footer height, page numbers, and whether page 1 is numbered. Select a block in the list and edit it in the property panel. The Data expander shows the data file name. Edit data opens one window: a tree of nodes on the left, and the selected node on the right. A plus on the root, an object, or a 1:N adds a field under that node. A collection such as invoice.items is a 1:N nested under the invoice object. The sample values live in that same file. Insert missing fields adds paths the open report binds and leaves values already set. The same data file can be opened with another report. Saving the report does not write the data into it. Reorder with Up and Down. The preview is the PDF, shown in WebView2, and it refreshes about half a second after the report or the data changes. Selecting a block does not reprint. WebView2 needs the WebView2 runtime, and PDF preview needs Chromium installed for the builder output.

## PDF on Azure

Playwright works in Azure Container Apps, Azure Container Instances, AKS, and App Service for Linux containers. It is a poor fit for Azure Functions and for Windows containers.

`PdfExportOptions.Container = true` launches Chromium with `--disable-dev-shm-usage` and `--no-sandbox`. Leave it off on Windows and on a normal VM. Container Apps cannot raise `/dev/shm` or install a custom seccomp profile, which is why those two flags exist. The HTML is the compiler's own output.

The repository `Dockerfile` publishes `samples/viewer-blazor` on `mcr.microsoft.com/dotnet/sdk:10.0`, then runs it on `mcr.microsoft.com/dotnet/aspnet:10.0` with Chromium installed at build time. The official `mcr.microsoft.com/playwright/dotnet` image is for development and may not include the .NET 10 runtime. Build it with Linux containers:

```powershell
docker build -t see-attached .
docker run --rm -p 8080:8080 see-attached
```

The page is `http://localhost:8080`. The image sets `REPORTCREATOR_CONTAINER=1`, so the sample passes `PdfExportOptions.Container`. Give the app at least 1 GB of memory, and 2 GB if several exports run at once. Keep one browser open and open a page per export.

## Fonts

Source Sans 3 regular and bold are embedded in the HTML (Latin subset, SIL Open Font License 1.1). The license is `src/ReportCreator.Compile/Fonts/OFL.txt`. Characters outside that subset fall back to the host's font, so a Linux PDF host can wrap those words differently.

## Dependencies

Nothing else is added without asking.

| Piece | Where | License |
| --- | --- | --- |
| Microsoft.Playwright | PDF library and PDF tests | Apache-2.0 |
| Microsoft.Web.WebView2 | WPF builder only | BSD-3-Clause |
| Source Sans 3 regular and bold | Vendored font files | OFL-1.1 |
| next, react, react-dom | Next.js sample only | MIT |
| typescript | Next.js sample, dev only | Apache-2.0 |
| @types/node, @types/react, @types/react-dom | Next.js sample, dev only | MIT |
| xunit.v3 | Tests only | Apache-2.0 |

Not used: QuestPDF, PdfSharp, iText, IronPDF, SelectPdf, PuppeteerSharp, paged.js, pdf.js, NJsonSchema, AvalonDock, or a commercial report suite.
