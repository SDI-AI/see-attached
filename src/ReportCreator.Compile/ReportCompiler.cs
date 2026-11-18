using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReportCreator.Model;

namespace ReportCreator.Compile;

public static partial class ReportCompiler
{
    private static readonly Lazy<string> FontFaces = new(LoadFontFaces);

    public static string Compile(ReportDefinition definition, JsonElement data, CompileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = ReportValidator.Validate(definition);
        if (errors.Count > 0)
            throw new ReportValidationException(errors);

        options ??= new CompileOptions();
        if (options.Parameters.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object))
            throw new ArgumentException("Report parameters must be a JSON object keyed by parameter name.");
        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            data = JsonSerializer.SerializeToElement(new { });

        var scope = DataScope.FromRoot(data);
        var size = PaperCatalog.Resolve(definition.Page);
        var margin = definition.Page.Margin;
        var headerMm = PageMetrics.Header(definition);
        var footerMm = PageMetrics.Footer(definition);
        var numberMm = PageMetrics.Numbers(definition.Page);
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"")
            .Append(Encode(options.Culture.Name.Length == 0 ? "en" : options.Culture.Name))
            .Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(Encode(definition.Name))
            .Append("</title><style>")
            .Append(FontFaces.Value)
            .Append(SheetCss(size, margin, numberMm, definition.Page.PageNumbers, definition.Page.OmitPageNumberOnFirstPage))
            .Append("</style></head><body");
        if (options.DesignMode)
            html.Append(" class=\"design\"");
        html.Append("><article class=\"sheet\" data-rc=\"1\">");
        // Chromium positions fixed elements inside the page area. A positive
        // bottom offset then sits on top of the body, because @page has already
        // reserved the footer band. A header and footer group take that space
        // in the table flow, so the body stops above the footer on every page.
        var framed = headerMm > 0 || footerMm > 0;
        if (framed)
        {
            html.Append("<table class=\"page-frame\">");
            if (headerMm > 0)
            {
                html.Append("<thead><tr><td><div class=\"page-chrome page-header\" style=\"height:")
                    .Append(Num(headerMm)).Append("mm\">");
                RenderBand(html, definition.PageHeader, definition, scope, options);
                html.Append("</div></td></tr></thead>");
            }

            if (footerMm > 0)
            {
                html.Append("<tfoot><tr><td><div class=\"page-chrome page-footer\" style=\"height:")
                    .Append(Num(footerMm)).Append("mm\">");
                RenderBand(html, definition.PageFooter, definition, scope, options);
                html.Append("</div></td></tr></tfoot>");
            }

            html.Append("<tbody><tr><td>");
        }

        RenderBand(html, definition.Header, definition, scope, options);
        RenderBand(html, definition.Body, definition, scope, options);
        RenderBand(html, definition.Footer, definition, scope, options);
        if (framed)
            html.Append("</td></tr></tbody></table>");

        html.Append("</article>");
        if (options.DesignMode)
            html.Append(DesignScript);
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void RenderBand(StringBuilder html, Band band, ReportDefinition definition, DataScope scope, CompileOptions options)
    {
        if (band.Blocks.Count == 0)
            return;

        html.Append("<section class=\"band\">");
        foreach (var block in band.Blocks)
            RenderBlock(html, block, definition, scope, options);
        html.Append("</section>");
    }

    private static void RenderBlock(StringBuilder html, Block block, ReportDefinition definition, DataScope scope, CompileOptions options)
    {
        if (!IsShown(block, definition, scope, options))
            return;

        var flags = Flags(block, options);
        switch (block)
        {
            case TextBlock text:
                html.Append("<div class=\"block")
                    .Append(flags)
                    .Append("\" data-id=\"").Append(Encode(text.Id))
                    .Append("\" style=\"").Append(Encode(TextStyle(text)))
                    .Append("\">");
                if (text.Runs is { Count: > 0 })
                {
                    foreach (var run in text.Runs)
                        AppendRun(html, run, definition, scope, options);
                }
                else
                    html.Append(Expand(text.Text, definition, scope, options));
                html.Append("</div>");
                break;
            case FieldBlock field:
                var fieldText = ResolveText(field.Path, field.Format, definition, scope, options);
                html.Append("<div class=\"block")
                    .Append(flags)
                    .Append("\" data-id=\"").Append(Encode(field.Id))
                    .Append("\" style=\"").Append(Encode(TextStyle(field.FontSizePt, field.Bold, field.Italic, field.Underline, field.FontFamily, field.LineHeight, field.Color, field.Align, field.Style)))
                    .Append("\">")
                    .Append(Encode(fieldText))
                    .Append("</div>");
                break;
            case RuleBlock rule:
                html.Append("<div class=\"block rule")
                    .Append(flags)
                    .Append("\" data-id=\"").Append(Encode(rule.Id))
                    .Append("\" style=\"").Append(Encode(RuleStyle(rule)))
                    .Append("\"></div>");
                break;
            case PanelBlock panel:
                html.Append("<div class=\"block panel")
                    .Append(flags)
                    .Append("\" data-id=\"").Append(Encode(panel.Id))
                    .Append('"');
                AppendStyleAttribute(html, BoxCss(panel.Style));
                html.Append('>');
                foreach (var child in panel.Blocks)
                    RenderBlock(html, child, definition, scope, options);
                html.Append("</div>");
                break;
            case ImageBlock image:
                var source = ImageSource(image, scope);
                if (!ImageSources.IsAllowed(source))
                    break;
                html.Append("<img class=\"block")
                    .Append(flags)
                    .Append("\" data-id=\"").Append(Encode(image.Id))
                    .Append("\" alt=\"").Append(Encode(image.Alt))
                    .Append("\" src=\"").Append(Encode(source)).Append('"');
                var imageStyle = new StringBuilder();
                if (image.WidthMm is decimal width)
                    imageStyle.Append("width:").Append(Num(width)).Append("mm;");
                if (image.HeightMm is decimal height)
                    imageStyle.Append("height:").Append(Num(height)).Append("mm;");
                if (image.Fit == ImageFit.Contain)
                    imageStyle.Append("object-fit:contain;object-position:").Append(AlignCss(image.Align)).Append(';');
                if (image.Align == TextAlign.Center)
                    imageStyle.Append("margin-left:auto;margin-right:auto;");
                else if (image.Align == TextAlign.Right)
                    imageStyle.Append("margin-left:auto;");
                imageStyle.Append(BoxCss(image.Style));
                if (imageStyle.Length > 0)
                    html.Append(" style=\"").Append(Encode(imageStyle.ToString())).Append('"');
                html.Append('>');
                break;
            case SpacerBlock spacer:
                html.Append("<div class=\"block spacer")
                    .Append(flags)
                    .Append("\" data-id=\"").Append(Encode(spacer.Id))
                    .Append("\" style=\"height:").Append(Num(spacer.HeightMm)).Append("mm;")
                    .Append(Encode(BoxCss(spacer.Style)))
                    .Append("\"></div>");
                break;
            case ColumnsBlock columns:
                html.Append("<div class=\"block cols cols-")
                    .Append(columns.Columns.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(flags)
                    .Append("\" data-id=\"").Append(Encode(columns.Id))
                    .Append('"');
                AppendStyleAttribute(html, ColumnGrid(columns) + BoxCss(columns.Style));
                html.Append('>');
                foreach (var column in columns.Columns)
                {
                    html.Append("<div class=\"col\">");
                    foreach (var child in column.Blocks)
                        RenderBlock(html, child, definition, scope, options);
                    html.Append("</div>");
                }
                html.Append("</div>");
                break;
            case RepeatBlock repeat:
                RenderRepeat(html, repeat, definition, scope, options, flags);
                break;
            case TableBlock table:
                RenderTable(html, table, scope, options, flags);
                break;
        }
    }

    private static string Flags(Block block, CompileOptions options)
    {
        var flags = "";
        if (block.KeepTogether)
            flags += " keep";
        if (block.PageBreakBefore)
            flags += " break-before";
        if (block.PageBreakAfter)
            flags += " break-after";
        if (block.Id == options.SelectedId)
            flags += " rc-selected";
        return flags;
    }

    private static void RenderRepeat(StringBuilder html, RepeatBlock repeat, ReportDefinition definition, DataScope scope, CompileOptions options, string flags)
    {
        var items = Rows(scope, repeat.DataPath);
        html.Append("<div class=\"block repeat").Append(flags).Append("\" data-id=\"").Append(Encode(repeat.Id)).Append("\">");
        for (var index = 0; index < items.Count; index++)
        {
            var itemClass = "repeat-item";
            if (repeat.KeepItemTogether)
                itemClass += " keep";
            if (repeat.BreakBeforeItem && index > 0)
                itemClass += " break-before";
            html.Append("<div class=\"").Append(itemClass).Append("\">");
            var itemScope = scope.ForItem(items[index]);
            foreach (var child in repeat.Blocks)
                RenderBlock(html, child, definition, itemScope, options);
            html.Append("</div>");
        }

        html.Append("</div>");
    }

    private static void RenderTable(StringBuilder html, TableBlock table, DataScope scope, CompileOptions options, string flags)
    {
        var rows = Rows(scope, table.DataPath);
        var identity = string.IsNullOrWhiteSpace(table.GroupKey) ? table.GroupBy : table.GroupKey;
        var titlePath = string.IsNullOrWhiteSpace(table.GroupBy) ? table.GroupKey : table.GroupBy;
        var groups = string.IsNullOrWhiteSpace(identity) ? [] : Groups(rows, identity, rawKey: !string.IsNullOrWhiteSpace(table.GroupKey));
        if (groups.Count == 0)
        {
            WriteTable(html, table, rows, rows, scope, options, "block" + flags, table.Id, groupTitle: null);
            return;
        }

        html.Append("<div class=\"block groups").Append(flags).Append("\" data-id=\"").Append(Encode(table.Id)).Append("\">");
        for (var index = 0; index < groups.Count; index++)
        {
            var wrap = "group-wrap";
            if (table.KeepGroupTogether)
                wrap += " keep";
            if (table.BreakBeforeGroup && index > 0)
                wrap += " break-before";
            html.Append("<div class=\"").Append(wrap).Append("\">");
            WriteTable(
                html,
                table,
                groups[index],
                index == groups.Count - 1 ? rows : null,
                scope,
                options,
                "group",
                id: null,
                GroupTitle(groups[index], titlePath!, options.Culture));
            html.Append("</div>");
        }

        html.Append("</div>");
    }

    private static void WriteTable(
        StringBuilder html,
        TableBlock table,
        List<JsonElement> rows,
        List<JsonElement>? reportRows,
        DataScope scope,
        CompileOptions options,
        string className,
        string? id,
        string? groupTitle)
    {
        var fixedLayout = table.Columns.Exists(column => column.WidthMm is > 0);
        html.Append("<table class=\"").Append(className);
        if (fixedLayout)
            html.Append(" layout-fixed");
        html.Append('"');
        if (id is not null)
            html.Append(" data-id=\"").Append(Encode(id)).Append('"');
        AppendStyleAttribute(html, TableCss(table.TableStyle) + BoxCss(table.Style));
        html.Append('>');
        html.Append("<colgroup>");
        foreach (var column in table.Columns)
        {
            html.Append("<col");
            if (column.WidthMm is decimal width)
                html.Append(" style=\"width:").Append(Num(width)).Append("mm\"");
            html.Append('>');
        }

        html.Append("</colgroup>");
        if (groupTitle is not null || table.ShowHeader)
        {
            html.Append("<thead>");
            if (groupTitle is not null)
            {
                html.Append("<tr class=\"group-head\"><th colspan=\"")
                    .Append(table.Columns.Count.ToString(CultureInfo.InvariantCulture))
                    .Append('"');
                AppendStyleAttribute(html, CellCss(table.TableStyle, header: true));
                html.Append('>')
                    .Append(Encode(groupTitle))
                    .Append("</th></tr>");
            }

            if (table.ShowHeader)
            {
                html.Append("<tr>");
                foreach (var column in table.Columns)
                {
                    html.Append("<th class=\"").Append(AlignClass(column.Align)).Append('"');
                    AppendStyleAttribute(html, CellCss(table.TableStyle, header: true));
                    html.Append('>').Append(Encode(column.Header)).Append("</th>");
                }

                html.Append("</tr>");
            }

            html.Append("</thead>");
        }

        html.Append("<tbody>");
        var running = new decimal[table.Columns.Count];
        foreach (var row in rows)
        {
            html.Append("<tr>");
            for (var index = 0; index < table.Columns.Count; index++)
            {
                var column = table.Columns[index];
                string cell;
                if (column.RunningTotal)
                {
                    if (TryCell(scope, row, column.Path, out var amount) && DataValues.AsDecimal(amount) is decimal number)
                        running[index] += number;
                    cell = DataValues.Format(JsonSerializer.SerializeToElement(running[index]), column.Format, options.Culture);
                }
                else
                {
                    cell = TryCell(scope, row, column.Path, out var value)
                        ? DataValues.Format(value, column.Format, options.Culture)
                        : "";
                }

                html.Append("<td class=\"").Append(AlignClass(column.Align)).Append('"');
                AppendStyleAttribute(html, CellCss(table.TableStyle, header: false));
                html.Append('>').Append(Encode(cell)).Append("</td>");
            }

            html.Append("</tr>");
        }

        if (table.GroupAggregates.Count > 0)
            AppendAggregateRow(html, table.Columns, table.GroupAggregates, rows, options.Culture, "group-foot", table.TableStyle);
        html.Append("</tbody>");

        if (reportRows is not null && table.Aggregates.Count > 0)
        {
            html.Append("<tfoot>");
            AppendAggregateRow(html, table.Columns, table.Aggregates, reportRows, options.Culture, null, table.TableStyle);
            html.Append("</tfoot>");
        }

        html.Append("</table>");
    }

    private static string GroupTitle(List<JsonElement> group, string groupBy, CultureInfo culture)
    {
        var key = DataValues.TryResolve(group[0], groupBy, out var keyValue)
            ? DataValues.Format(keyValue, null, culture)
            : "";
        return key.Length == 0 ? "(blank)" : key;
    }

    private static bool TryCell(DataScope scope, JsonElement row, string path, out JsonElement value)
    {
        if (scope.Item is not null && DataScope.TryScopePrefix(path, out _, out _))
            return scope.TryGet(path, out value);
        return DataValues.TryResolve(row, path, out value);
    }

    private static List<JsonElement> Rows(DataScope scope, string path)
    {
        var rows = new List<JsonElement>();
        if (!scope.TryGet(path, out var array) || array.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var row in array.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object)
                rows.Add(row);
        }

        return rows;
    }

    private static List<List<JsonElement>> Groups(List<JsonElement> rows, string? groupBy, bool rawKey)
    {
        if (string.IsNullOrWhiteSpace(groupBy))
            return rows.Count == 0 ? [] : [rows];

        var groups = new List<List<JsonElement>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = DataValues.TryResolve(row, groupBy, out var value)
                ? rawKey ? RawKey(value) : DataValues.Format(value, null, CultureInfo.InvariantCulture)
                : "";
            if (!index.TryGetValue(key, out var at))
            {
                at = groups.Count;
                index[key] = at;
                groups.Add([]);
            }

            groups[at].Add(row);
        }

        return groups;
    }

    private static string RawKey(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "s:" + (value.GetString() ?? ""),
        JsonValueKind.Number => "n:" + value.GetRawText(),
        JsonValueKind.True => "b:true",
        JsonValueKind.False => "b:false",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => "j:" + value.GetRawText()
    };

    private static void AppendAggregateRow(
        StringBuilder html,
        List<TableColumn> columns,
        List<TableAggregate> aggregates,
        List<JsonElement> rows,
        CultureInfo culture,
        string? rowClass,
        TableStyle? tableStyle)
    {
        html.Append("<tr");
        if (rowClass is not null)
            html.Append(" class=\"").Append(rowClass).Append('"');
        html.Append('>');
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var aggregate = aggregates.FirstOrDefault(item => item.ColumnPath == column.Path);
            var cell = aggregate is null
                ? index == 0 ? aggregates[0].Label ?? "" : ""
                : Aggregate(aggregate, rows, culture);
            html.Append("<td class=\"").Append(AlignClass(column.Align)).Append('"');
            AppendStyleAttribute(html, CellCss(tableStyle, header: false));
            html.Append('>').Append(Encode(cell)).Append("</td>");
        }

        html.Append("</tr>");
    }

    private static bool IsShown(Block block, ReportDefinition definition, DataScope scope, CompileOptions options)
    {
        if (block.When is null || string.IsNullOrWhiteSpace(block.When.Path))
            return true;

        var found = TryResolveValue(block.When.Path, definition, scope, options, out var value);
        var empty = !found || IsEmpty(value);
        return block.When.Kind switch
        {
            ShowWhenKind.Empty => empty,
            ShowWhenKind.NotEmpty => !empty,
            ShowWhenKind.Equals => found && Compare(value, block.When.Value) == 0,
            ShowWhenKind.NotEquals => !found || Compare(value, block.When.Value) != 0,
            ShowWhenKind.GreaterThan => found && Compare(value, block.When.Value) > 0,
            ShowWhenKind.LessThan => found && Compare(value, block.When.Value) < 0,
            _ => true
        };
    }

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        _ => false
    };

    private static int Compare(JsonElement value, string? expected)
    {
        expected ??= "";
        if (DataValues.AsDecimal(value) is decimal number
            && decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var target))
            return number.CompareTo(target);

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        return string.Compare(text, expected, StringComparison.Ordinal);
    }

    private static string Aggregate(TableAggregate aggregate, List<JsonElement> rows, CultureInfo culture)
    {
        var numbers = new List<decimal>();
        var present = 0;
        foreach (var row in rows)
        {
            if (!DataValues.TryResolve(row, aggregate.ColumnPath, out var value))
                continue;
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            present++;
            if (DataValues.AsDecimal(value) is decimal number)
                numbers.Add(number);
        }

        decimal result = aggregate.Kind switch
        {
            AggregateKind.Count => present,
            AggregateKind.Avg => numbers.Count == 0 ? 0 : numbers.Sum() / numbers.Count,
            _ => numbers.Sum()
        };

        return DataValues.Format(JsonSerializer.SerializeToElement(result), aggregate.Format, culture);
    }

    private static string Expand(string? text, ReportDefinition definition, DataScope scope, CompileOptions options)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var html = new StringBuilder();
        var index = 0;
        foreach (Match match in Token().Matches(text))
        {
            html.Append(Encode(text[index..match.Index]));
            var path = match.Groups[1].Value;
            var format = match.Groups[2].Success ? match.Groups[2].Value : null;
            html.Append(Encode(ResolveText(path, format, definition, scope, options)));
            index = match.Index + match.Length;
        }

        html.Append(Encode(text[index..]));
        return html.ToString();
    }

    private static string ResolveText(string path, string? format, ReportDefinition definition, DataScope scope, CompileOptions options)
    {
        if (path == PrintPaths.ReportName)
            return definition.Name;
        if (path is PrintPaths.Date or PrintPaths.Time)
            return FormatClock(options, format, path == PrintPaths.Date);
        if (!TryResolveValue(path, definition, scope, options, out var value))
            return "";
        return DataValues.Format(value, format, options.Culture);
    }

    private static bool TryResolveValue(string path, ReportDefinition definition, DataScope scope, CompileOptions options, out JsonElement value)
    {
        if (PrintPaths.IsPrintField(path))
        {
            value = PrintElement(path, definition, options);
            return true;
        }

        if (PrintPaths.TryParameterName(path, out var name))
            return ParameterValues.TryGet(definition, options.Parameters, name, out value);

        return scope.TryGet(path, out value);
    }

    private static JsonElement PrintElement(string path, ReportDefinition definition, CompileOptions options)
    {
        var moment = (options.Clock ?? DateTimeOffset.Now).DateTime;
        var text = path switch
        {
            PrintPaths.Date => moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PrintPaths.Time => moment.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            _ => definition.Name
        };
        return JsonSerializer.SerializeToElement(text);
    }

    private static string FormatClock(CompileOptions options, string? format, bool date)
    {
        var moment = (options.Clock ?? DateTimeOffset.Now).DateTime;
        var pattern = string.IsNullOrWhiteSpace(format) ? date ? "d" : "t" : format;
        try
        {
            return moment.ToString(pattern, options.Culture);
        }
        catch (FormatException)
        {
            return moment.ToString(date ? "d" : "t", options.Culture);
        }
    }

    private static string SheetCss(PageSize size, Margin margin, decimal numberMm, PageNumberPlacement numbers, bool omitFirstPageNumber) =>
        $$"""
        html, body { margin: 0; padding: 0; }
        body {
          background: #e5e7eb;
          color: #111827;
          font-family: "Source Sans 3", sans-serif;
          font-size: 11pt;
          line-height: 1.4;
          -webkit-print-color-adjust: exact;
          print-color-adjust: exact;
        }
        .sheet {
          box-sizing: border-box;
          width: {{Num(size.WidthMm)}}mm;
          min-height: {{Num(size.HeightMm)}}mm;
          margin: 16px auto;
          padding: {{Num(margin.Top)}}mm {{Num(margin.Right)}}mm {{Num(margin.Bottom)}}mm {{Num(margin.Left)}}mm;
          background: #fff;
          box-shadow: 0 1px 4px rgba(0, 0, 0, 0.18);
        }
        .block { margin: 0 0 2mm; }
        .block:last-child { margin-bottom: 0; }
        .keep, tr { break-inside: avoid; page-break-inside: avoid; }
        table.page-frame { width: 100%; border-collapse: collapse; }
        table.page-frame > thead { display: table-header-group; }
        table.page-frame > tfoot { display: table-footer-group; }
        table.page-frame > thead > tr > td, table.page-frame > tfoot > tr > td, table.page-frame > tbody > tr > td { padding: 0; border: 0; background: none; vertical-align: top; }
        table.page-frame > tfoot > tr > td { font-weight: normal; }
        table.page-frame > tbody > tr { break-inside: auto; page-break-inside: auto; }
        .page-chrome { overflow: hidden; }
        table.layout-fixed { table-layout: fixed; }
        .break-before { break-before: page; page-break-before: always; }
        .break-after { break-after: page; page-break-after: always; }
        .repeat-item { margin: 0 0 4mm; }
        .repeat-item:last-child { margin-bottom: 0; }
        .group-wrap { margin: 0 0 4mm; }
        .group-wrap:last-child { margin-bottom: 0; }
        tr.group-head { break-after: avoid; }
        tr.group-head th, tr.group-head td { font-weight: 700; background: #f9fafb; border-bottom: 0.3mm solid #111827; }
        tr.group-foot td { font-weight: 700; border-top: 0.2mm solid #9ca3af; }
        .cols { display: grid; gap: 8mm; align-items: start; }
        .cols-2 { grid-template-columns: minmax(0,1fr) minmax(0,1fr); }
        .cols-3 { grid-template-columns: minmax(0,1fr) minmax(0,1fr) minmax(0,1fr); }
        table { width: 100%; border-collapse: collapse; }
        thead { display: table-header-group; }
        th, td { padding: 1.5mm 2mm; border-bottom: 1px solid #e5e7eb; vertical-align: top; }
        th { font-weight: 700; background: #f3f4f6; text-align: left; }
        tfoot td { font-weight: 700; border-top: 0.4mm solid #111827; border-bottom: 0; }
        .right { text-align: right; }
        .center { text-align: center; }
        img.block { display: block; max-width: 100%; }
        .rc-selected { outline: 0.6mm solid #2563eb; outline-offset: 0.4mm; }
        body.design .block { cursor: pointer; outline: 1px solid #d1d5db; }
        body.design .block.rc-selected { outline: 0.6mm solid #2563eb; outline-offset: 0.4mm; }
        {{PageRule(size, margin, numberMm, numbers, omitFirstPageNumber)}}
        @media print {
          body { background: #fff; }
          .sheet { width: auto; min-height: 0; margin: 0; padding: 0; box-shadow: none; }
          .rc-selected, body.design .block { outline: none; }
        }
        """;

    private static string PageRule(PageSize size, Margin margin, decimal numberMm, PageNumberPlacement numbers, bool omitFirstPageNumber)
    {
        var top = margin.Top;
        var bottom = margin.Bottom + numberMm;
        var rule = new StringBuilder();
        rule.Append("@page { size: ").Append(Num(size.WidthMm)).Append("mm ").Append(Num(size.HeightMm))
            .Append("mm; margin: ").Append(Num(top)).Append("mm ").Append(Num(margin.Right))
            .Append("mm ").Append(Num(bottom)).Append("mm ").Append(Num(margin.Left)).Append("mm;");
        var slot = numbers switch
        {
            PageNumberPlacement.BottomLeft => "bottom-left",
            PageNumberPlacement.BottomRight => "bottom-right",
            PageNumberPlacement.None => null,
            _ => "bottom-center"
        };
        if (slot is not null)
        {
            rule.Append(" @").Append(slot)
                .Append(" { content: \"Page \" counter(page) \" of \" counter(pages); font-family: \"Source Sans 3\", sans-serif; font-size: 9pt; color: #6b7280; }");
        }

        rule.Append(" }");
        if (slot is not null && omitFirstPageNumber)
            rule.Append(" @page :first { @").Append(slot).Append(" { content: none; } }");
        return rule.ToString();
    }

    private static string AlignCss(TextAlign align) => align switch
    {
        TextAlign.Center => "center",
        TextAlign.Right => "right",
        _ => "left"
    };

    private static string AlignClass(TextAlign align) => align switch
    {
        TextAlign.Center => "center",
        TextAlign.Right => "right",
        _ => "left"
    };

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string LoadFontFaces()
    {
        var regular = FontBase64("SourceSans3-Regular.woff2");
        var bold = FontBase64("SourceSans3-Bold.woff2");
        return
            "@font-face{font-family:\"Source Sans 3\";font-style:normal;font-weight:400;font-display:swap;src:url(data:font/woff2;base64,"
            + regular
            + ") format(\"woff2\");}"
            + "@font-face{font-family:\"Source Sans 3\";font-style:normal;font-weight:700;font-display:swap;src:url(data:font/woff2;base64,"
            + bold
            + ") format(\"woff2\");}";
    }

    private static string FontBase64(string fileName)
    {
        var assembly = typeof(ReportCompiler).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing font resource {fileName}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Convert.ToBase64String(buffer.ToArray());
    }

    private const string DesignScript = """
        <script>
        (function () {
          document.addEventListener("click", function (event) {
            var target = event.target;
            if (!target || !target.closest) return;
            var node = target.closest("[data-id]");
            if (!node || !window.chrome || !window.chrome.webview) return;
            window.chrome.webview.postMessage(JSON.stringify({ type: "select", id: node.getAttribute("data-id") }));
          }, true);
        })();
        </script>
        """;

    [GeneratedRegex("\\{([A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*)(?::([^{}]+))?\\}")]
    private static partial Regex Token();
}
