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
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"")
            .Append(Encode(options.Culture.Name.Length == 0 ? "en" : options.Culture.Name))
            .Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(Encode(definition.Name))
            .Append("</title><style>")
            .Append(FontFaces.Value)
            .Append(SheetCss(size, margin))
            .Append("</style></head><body");
        if (options.DesignMode)
            html.Append(" class=\"design\"");
        html.Append("><article class=\"sheet\" data-rc=\"1\">");

        RenderBand(html, definition.Header, data, options);
        RenderBand(html, definition.Body, data, options);
        RenderBand(html, definition.Footer, data, options);
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
        foreach (var row in rows)
        {
            html.Append("<tr>");
            foreach (var column in table.Columns)
            {
                var cell = DataValues.TryResolve(row, column.Path, out var value)
                    ? DataValues.Format(value, column.Format, options.Culture)
                    : "";
                html.Append("<td class=\"").Append(AlignClass(column.Align)).Append("\">")
                    .Append(Encode(cell))
                    .Append("</td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody>");

        if (table.Aggregates.Count > 0)
        {
            html.Append("<tfoot><tr>");
            for (var index = 0; index < table.Columns.Count; index++)
            {
                var column = table.Columns[index];
                var aggregate = table.Aggregates.FirstOrDefault(item => item.ColumnPath == column.Path);
                string cell;
                if (aggregate is null)
                    cell = index == 0 ? table.Aggregates[0].Label ?? "" : "";
                else
                    cell = Aggregate(aggregate, rows, options.Culture);
                html.Append("<td class=\"").Append(AlignClass(column.Align)).Append("\">")
                    .Append(Encode(cell))
                    .Append("</td>");
            }
            html.Append("</tr></tfoot>");
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

    private static string SheetCss(PageSize size, Margin margin) =>
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
        @page { size: {{Num(size.WidthMm)}}mm {{Num(size.HeightMm)}}mm; margin: {{Num(margin.Top)}}mm {{Num(margin.Right)}}mm {{Num(margin.Bottom)}}mm {{Num(margin.Left)}}mm; }
        @media print {
          body { background: #fff; }
          .sheet { width: auto; min-height: 0; margin: 0; padding: 0; box-shadow: none; }
          .rc-selected, body.design .block { outline: none; }
        }
        """;

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
