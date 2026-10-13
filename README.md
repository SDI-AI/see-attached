# Report Creator

Open-source builder for business documents such as invoices, statements, and lists. A report is a header, a body that flows, and a footer. Tables split by row. It is not a free canvas and not a pixel-perfect form.

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

## What v1 does

- Paper sizes A3, A4, A5, Letter, Legal, Tabloid, and custom. Portrait or landscape. Lengths are millimetres, with at most two decimal places.
- Bands: report header, body, and report footer. Each appears once. There is no repeating page header or footer, and no "page X of Y".
- Blocks: text, bound field, image, spacer, a row of two or three columns, and a table bound to an array.
- Bindings are property paths (`customer.name`) and .NET numeric or date format strings. The culture is chosen when you compile. Table aggregates are Sum, Count, and Avg. Values are HTML-encoded.
- Images are `https` URLs or `data:image` PNG, JPEG, GIF, or WebP.
- Print CSS sets `@page` size and margin, repeats a table header with `thead { display: table-header-group }`, and keeps a table row or a `keepTogether` block from splitting.
- Screen preview is one paper-width column. Chromium inserts page breaks only when you print or export PDF. Those two views will not show the same break positions.

Not in v1: grouping, charts, barcodes, rich text, expressions, right-to-left layout, and a measured stack of pages in the viewer.

## Projects

| Project | Role |
| --- | --- |
| `src/ReportCreator.Model` | Papers, validation, JSON model |
| `src/ReportCreator.Compile` | Definition plus data to HTML |
| `src/ReportCreator.Pdf` | Playwright PDF export |
| `src/ReportCreator.Blazor` | Iframe viewer component |
| `src/ReportCreator.Builder` | WPF builder |
| `samples/viewer-blazor` | Blazor host for the invoice fixture |
| `samples/viewer-next` | Next.js host for the same HTML |
| `samples/render-fixture` | Writes `samples/viewer-next/public/invoice.html` |
| `fixtures/invoice` | Invoice definition and data |
| `tests/ReportCreator.Compile.Tests` | HTML checks, no browser |
| `tests/ReportCreator.Pdf.Tests` | PDF page size and text |

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

The WPF builder is `src/ReportCreator.Builder`. File menu: new report, the invoice sample, open and save JSON or a `.rcreport` zip, export HTML, and export PDF. Page setup edits paper, orientation, and margins. Select a block in the list or click it in the preview, then edit it in the property panel. Reorder with Up and Down. The preview is WebView2. PDF export uses the same Playwright library as the server. WebView2 needs the WebView2 runtime installed on the machine.

## PDF on Azure

Playwright works in Azure Container Apps, Azure Container Instances, AKS, and App Service for Linux containers. It is a poor fit for Azure Functions and for Windows containers.

`PdfExportOptions.Container = true` launches Chromium with `--disable-dev-shm-usage` and `--no-sandbox`. Leave it off on Windows and on a normal VM. Container Apps cannot raise `/dev/shm` or install a custom seccomp profile, which is why those two flags exist. The HTML is the compiler's own output.

Build the image from `mcr.microsoft.com/dotnet/aspnet:10.0` and install Chromium during the image build. The official `mcr.microsoft.com/playwright/dotnet` image is for development and may not include the .NET 10 runtime. Give the app at least 1 GB of memory, and 2 GB if several exports run at once. Keep one browser open and open a page per export.

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
