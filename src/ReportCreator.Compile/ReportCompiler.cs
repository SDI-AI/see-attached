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
        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            data = JsonSerializer.SerializeToElement(new { });

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
            .Append(SheetCss(size, margin, headerMm, footerMm, numberMm, definition.Page.PageNumbers))
            .Append("</style></head><body");
        if (options.DesignMode)
            html.Append(" class=\"design\"");
        html.Append("><article class=\"sheet\" data-rc=\"1\">");
        if (headerMm > 0)
        {
            html.Append("<header class=\"page-chrome page-header\">");
            RenderBand(html, definition.PageHeader, data, options);
            html.Append("</header>");
        }

        RenderBand(html, definition.Header, data, options);
        RenderBand(html, definition.Body, data, options);
        RenderBand(html, definition.Footer, data, options);
        if (footerMm > 0)
        {
            html.Append("<footer class=\"page-chrome page-footer\">");
            RenderBand(html, definition.PageFooter, data, options);
            html.Append("</footer>");
        }

        html.Append("</article>");
        if (options.DesignMode)
            html.Append(DesignScript);
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void RenderBand(StringBuilder html, Band band, JsonElement data, CompileOptions options)
    {
        if (band.Blocks.Count == 0)
            return;

        html.Append("<section class=\"band\">");
        foreach (var block in band.Blocks)
            RenderBlock(html, block, data, options);
        html.Append("</section>");
    }

    private static void RenderBlock(StringBuilder html, Block block, JsonElement data, CompileOptions options)
    {
        if (!IsShown(block, data))
            return;

        var selected = block.Id == options.SelectedId ? " rc-selected" : "";
        var keep = block.KeepTogether ? " keep" : "";
        switch (block)
        {
            case TextBlock text:
                html.Append("<div class=\"block")
                    .Append(keep).Append(selected)
                    .Append("\" data-id=\"").Append(Encode(text.Id))
                    .Append("\" style=\"").Append(StyleOf(text.FontSizePt, text.Bold, text.Color, text.Align))
                    .Append("\">")
                    .Append(Expand(text.Text, data, options.Culture))
                    .Append("</div>");
                break;
            case FieldBlock field:
                var fieldText = DataValues.TryResolve(data, field.Path, out var fieldValue)
                    ? DataValues.Format(fieldValue, field.Format, options.Culture)
                    : "";
                html.Append("<div class=\"block")
                    .Append(keep).Append(selected)
                    .Append("\" data-id=\"").Append(Encode(field.Id))
                    .Append("\" style=\"").Append(StyleOf(field.FontSizePt, field.Bold, field.Color, field.Align))
                    .Append("\">")
                    .Append(Encode(fieldText))
                    .Append("</div>");
                break;
            case ImageBlock image:
                html.Append("<img class=\"block")
                    .Append(keep).Append(selected)
                    .Append("\" data-id=\"").Append(Encode(image.Id))
                    .Append("\" alt=\"").Append(Encode(image.Alt))
                    .Append('"');
                if (ImageSources.IsAllowed(image.Source))
                    html.Append(" src=\"").Append(Encode(image.Source)).Append('"');
                var imageStyle = new StringBuilder();
                if (image.WidthMm is decimal width)
                    imageStyle.Append("width:").Append(Num(width)).Append("mm;");
                if (image.HeightMm is decimal height)
                    imageStyle.Append("height:").Append(Num(height)).Append("mm;");
                if (imageStyle.Length > 0)
                    html.Append(" style=\"").Append(imageStyle).Append('"');
                html.Append('>');
                break;
            case SpacerBlock spacer:
                html.Append("<div class=\"block spacer")
                    .Append(keep).Append(selected)
                    .Append("\" data-id=\"").Append(Encode(spacer.Id))
                    .Append("\" style=\"height:").Append(Num(spacer.HeightMm)).Append("mm\"></div>");
                break;
            case ColumnsBlock columns:
                html.Append("<div class=\"block cols cols-")
                    .Append(columns.Columns.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(keep).Append(selected)
                    .Append("\" data-id=\"").Append(Encode(columns.Id))
                    .Append("\">");
                foreach (var column in columns.Columns)
                {
                    html.Append("<div class=\"col\">");
                    foreach (var child in column.Blocks)
                        RenderBlock(html, child, data, options);
                    html.Append("</div>");
                }
                html.Append("</div>");
                break;
            case TableBlock table:
                RenderTable(html, table, data, options, keep, selected);
                break;
        }
    }

    private static void RenderTable(StringBuilder html, TableBlock table, JsonElement data, CompileOptions options, string keep, string selected)
    {
        var rows = Rows(data, table.DataPath);
        html.Append("<table class=\"block")
            .Append(keep).Append(selected)
            .Append("\" data-id=\"").Append(Encode(table.Id)).Append("\">");
        if (table.ShowHeader)
        {
            html.Append("<thead><tr>");
            foreach (var column in table.Columns)
            {
                html.Append("<th class=\"").Append(AlignClass(column.Align)).Append('"');
                if (column.WidthMm is decimal width)
                    html.Append(" style=\"width:").Append(Num(width)).Append("mm\"");
                html.Append('>').Append(Encode(column.Header)).Append("</th>");
            }
            html.Append("</tr></thead>");
        }

        html.Append("<tbody>");
        var running = new decimal[table.Columns.Count];
        foreach (var group in Groups(rows, table.GroupBy))
        {
            Array.Clear(running);
            if (!string.IsNullOrWhiteSpace(table.GroupBy))
            {
                var key = DataValues.TryResolve(group[0], table.GroupBy, out var keyValue)
                    ? DataValues.Format(keyValue, null, options.Culture)
                    : "";
                html.Append("<tr class=\"group-head\"><td colspan=\"")
                    .Append(table.Columns.Count.ToString(CultureInfo.InvariantCulture))
                    .Append("\">")
                    .Append(Encode(key.Length == 0 ? "(blank)" : key))
                    .Append("</td></tr>");
            }

            foreach (var row in group)
            {
                html.Append("<tr>");
                for (var index = 0; index < table.Columns.Count; index++)
                {
                    var column = table.Columns[index];
                    string cell;
                    if (column.RunningTotal)
                    {
                        if (DataValues.TryResolve(row, column.Path, out var amount) && DataValues.AsDecimal(amount) is decimal number)
                            running[index] += number;
                        cell = DataValues.Format(JsonSerializer.SerializeToElement(running[index]), column.Format, options.Culture);
                    }
                    else
                    {
                        cell = DataValues.TryResolve(row, column.Path, out var value)
                            ? DataValues.Format(value, column.Format, options.Culture)
                            : "";
                    }

                    html.Append("<td class=\"").Append(AlignClass(column.Align)).Append("\">")
                        .Append(Encode(cell))
                        .Append("</td>");
                }

                html.Append("</tr>");
            }

            if (table.GroupAggregates.Count > 0)
                AppendAggregateRow(html, table.Columns, table.GroupAggregates, group, options.Culture, "group-foot");
        }

        html.Append("</tbody>");

        if (table.Aggregates.Count > 0)
        {
            html.Append("<tfoot>");
            AppendAggregateRow(html, table.Columns, table.Aggregates, rows, options.Culture, null);
            html.Append("</tfoot>");
        }

        html.Append("</table>");
    }

    private static List<JsonElement> Rows(JsonElement data, string path)
    {
        var rows = new List<JsonElement>();
        if (!DataValues.TryResolve(data, path, out var array) || array.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var row in array.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object)
                rows.Add(row);
        }

        return rows;
    }

    private static List<List<JsonElement>> Groups(List<JsonElement> rows, string? groupBy)
    {
        if (string.IsNullOrWhiteSpace(groupBy))
            return rows.Count == 0 ? [] : [rows];

        var groups = new List<List<JsonElement>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = DataValues.TryResolve(row, groupBy, out var value)
                ? DataValues.Format(value, null, CultureInfo.InvariantCulture)
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

    private static void AppendAggregateRow(
        StringBuilder html,
        List<TableColumn> columns,
        List<TableAggregate> aggregates,
        List<JsonElement> rows,
        CultureInfo culture,
        string? rowClass)
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
            html.Append("<td class=\"").Append(AlignClass(column.Align)).Append("\">")
                .Append(Encode(cell))
                .Append("</td>");
        }

        html.Append("</tr>");
    }

    private static bool IsShown(Block block, JsonElement data)
    {
        if (block.When is null || string.IsNullOrWhiteSpace(block.When.Path))
            return true;

        var found = DataValues.TryResolve(data, block.When.Path, out var value);
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

    private static string Expand(string? text, JsonElement data, CultureInfo culture)
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
            var rendered = DataValues.TryResolve(data, path, out var value)
                ? DataValues.Format(value, format, culture)
                : "";
            html.Append(Encode(rendered));
            index = match.Index + match.Length;
        }

        html.Append(Encode(text[index..]));
        return html.ToString();
    }

    private static string SheetCss(PageSize size, Margin margin, decimal headerMm, decimal footerMm, decimal numberMm, PageNumberPlacement numbers) =>
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
        .keep, tr { break-inside: avoid; }
        tr.group-head { break-after: avoid; }
        tr.group-head td { font-weight: 700; background: #f9fafb; border-bottom: 0.3mm solid #111827; }
        tr.group-foot td { font-weight: 700; border-top: 0.2mm solid #9ca3af; }
        .cols { display: grid; gap: 8mm; }
        .cols-2 { grid-template-columns: 1fr 1fr; }
        .cols-3 { grid-template-columns: 1fr 1fr 1fr; }
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
        {{PageRule(size, margin, headerMm, footerMm, numberMm, numbers)}}
        @media print {
          body { background: #fff; }
          .sheet { width: auto; min-height: 0; margin: 0; padding: 0; box-shadow: none; }
          .rc-selected, body.design .block { outline: none; }
          {{FixedChrome(margin, headerMm, footerMm, numberMm)}}
        }
        """;

    private static string PageRule(PageSize size, Margin margin, decimal headerMm, decimal footerMm, decimal numberMm, PageNumberPlacement numbers)
    {
        var top = margin.Top + headerMm;
        var bottom = margin.Bottom + footerMm + numberMm;
        var rule = new StringBuilder();
        rule.Append("@page { size: ").Append(Num(size.WidthMm)).Append("mm ").Append(Num(size.HeightMm))
            .Append("mm; margin: ").Append(Num(top)).Append("mm ").Append(Num(margin.Right))
            .Append("mm ").Append(Num(bottom)).Append("mm ").Append(Num(margin.Left)).Append("mm;");
        if (numbers != PageNumberPlacement.None)
        {
            var slot = numbers switch
            {
                PageNumberPlacement.BottomLeft => "bottom-left",
                PageNumberPlacement.BottomRight => "bottom-right",
                _ => "bottom-center"
            };
            rule.Append(" @").Append(slot)
                .Append(" { content: \"Page \" counter(page) \" of \" counter(pages); font-family: \"Source Sans 3\", sans-serif; font-size: 9pt; color: #6b7280;");
            if (footerMm > 0)
                rule.Append(" margin-top: ").Append(Num(footerMm)).Append("mm;");
            rule.Append(" }");
        }

        rule.Append(" }");
        return rule.ToString();
    }

    private static string FixedChrome(Margin margin, decimal headerMm, decimal footerMm, decimal numberMm)
    {
        var css = new StringBuilder();
        if (headerMm > 0)
        {
            css.Append(".page-header { position: fixed; overflow: hidden; top: ").Append(Num(margin.Top))
                .Append("mm; left: ").Append(Num(margin.Left)).Append("mm; right: ").Append(Num(margin.Right))
                .Append("mm; height: ").Append(Num(headerMm)).Append("mm; }");
        }

        if (footerMm > 0)
        {
            css.Append(".page-footer { position: fixed; overflow: hidden; bottom: ").Append(Num(margin.Bottom + numberMm))
                .Append("mm; left: ").Append(Num(margin.Left)).Append("mm; right: ").Append(Num(margin.Right))
                .Append("mm; height: ").Append(Num(footerMm)).Append("mm; }");
        }

        return css.ToString();
    }

    private static string StyleOf(decimal fontSizePt, bool bold, string? color, TextAlign align)
    {
        var css = new StringBuilder();
        css.Append("font-size:").Append(Num(fontSizePt)).Append("pt;text-align:").Append(AlignCss(align)).Append(';');
        if (bold)
            css.Append("font-weight:700;");
        if (StyleRules.IsHexColor(color))
            css.Append("color:").Append(color).Append(';');
        css.Append("white-space:pre-wrap;");
        return css.ToString();
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
