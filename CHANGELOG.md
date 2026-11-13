# Changelog

Versions follow the sections already used in the README: v1, 1.1, 1.2, and 1.21. This file starts at 1.21, the state of `main` at `33f5f33` (2026-10-13).

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
