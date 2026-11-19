# Changelog

Versions follow the sections already used in the README: v1, 1.1, 1.2, 1.21, 1.22, 1.23, 1.24, 1.25, 1.26, 1.26.1, 1.27, and 1.28. This file starts at 1.21, the state of `main` at `33f5f33` (2026-10-13).

## 1.28.0

- A report can include a barcode. The value is a bound path or a literal, and the compiler writes inline SVG. Code 128, Code 39, QR Code, EAN-13, UPC-A, Data Matrix, and PDF417 are supported. Width, height, a text line under linear codes, QR error correction, colors, quiet zone, and alignment are saved on the block.
- An empty or rejected value draws an "Invalid barcode" box and records a warning when `CompileOptions.Warnings` is set. The compile does not fail.
- Schema version 6 is current. Versions 1 through 5 still compile. Saving a report that has a barcode writes version 6.
- `fixtures/barcode` is a sample shipping label. The values are synthetic.
- Barcode matrices come from ZXing.Net, Apache-2.0. The compiler draws the SVG itself.

## 1.27.0

- A page can carry a watermark, text or an image, for migrating Crystal Reports underlay watermarks. It repeats behind the body on every printed page. Alignment, font size up to 120 pt, color, opacity, and rotation are saved with the page. It takes no flow height, so page breaks and page numbers stay the same. Leave it off and an older report is unchanged.
- Schema version 5 is current. Versions 1 through 4 still compile. Saving a report that has a watermark writes version 5.
- `fixtures/watermark` is a sample notice with a centered grey DRAFT mark.

## 1.26.1

- Style attributes are HTML-encoded. A font stack such as `Arial, "Source Sans 3", sans-serif` no longer ends the attribute early and drops the CSS that follows. This covers the shared style writer and the text, field, rule, image, and spacer blocks.
- The repeating page footer is normal weight. The table-total rule still bolds an aggregate row, and it no longer bolds or clips the page footer.

## 1.26

- An image `sourcePath` reads a PNG, JPEG, GIF, or WebP data URI from the record. `fit: contain` keeps the aspect ratio. A missing or hidden image is left out.
- Text runs carry bold, italic, and underline, and can bind a value. They are not raw HTML.
- Schema version 4 is current. Versions 1 through 3 still compile.

## 1.25

- A repeat block renders its blocks for each object in an array. Repeats nest. Bindings use `item`, `parent`, and `root`. Each item can stay on one page, and a later item can start a new page.
- A table `groupKey` keeps rows apart when they share a `groupBy` label.
- Schema version 3 is current. Versions 1 and 2 still compile.

## 1.24

- Columns take a millimetre width or a weight, from one column up to eight. The unset column uses the remaining width. A columns block can contain one more columns block.

## 1.23

- Text and fields can set font family, italic, underline, and line height. Blocks can set margin, padding, border, and background. A rule draws a horizontal line. A panel groups blocks inside a border.
- Tables can set a font, cell padding, and cell borders.
- Schema version 2 is current. Version 1 reports still compile.

## 1.22

- The repeating page header and page footer are table groups. They keep their band clear of the body on every page, including the first. Page numbers stay in the bottom margin.
- A table writes a `colgroup`, so column widths apply when the header is hidden and when a group title spans the row.

## 1.21

- A data file is its own typed JSON document, saved as `.rcdata`. It is not stored inside the report, and any report can compile its values.
- Fields are string, number, date, bool, object, or array. Open still accepts a plain JSON object of values.

## 1.2

- A block can break the page before itself or after itself.
- A grouped table is one table per group. The group title and the column header both sit in the table head, so they repeat when the group crosses a page.
- A later group can start on a new page. A group stays on one page when it fits.

## 1.1

- Parameters are string, number, date, or bool, each with an optional default.
- Print fields `{report.name}`, `{print.date}`, and `{print.time}`.
- Page setup can leave the page number off the first page.

## v1

- JSON report definition plus JSON data compile to self-contained HTML on .NET 10. Playwright prints the same HTML to PDF.
- Repeating page header and page footer, with "Page N of M".
- Tables with grouping, running totals, Sum, Count, and Avg, and repeated column headings.
- WPF builder, Blazor viewer, and Next.js display sample.
