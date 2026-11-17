using System.Globalization;
using System.Text;
using System.Text.Json;
using ReportCreator.Model;

namespace ReportCreator.Compile;

public static partial class ReportCompiler
{
    private static string TextStyle(TextBlock text)
    {
        var css = TextStyle(text.FontSizePt, text.Bold, text.Italic, text.Underline, text.FontFamily, text.LineHeight, text.Color, text.Align, text.Style);
        if (text.SpaceBeforeMm is decimal before)
            css += "margin-top:" + Num(before) + "mm;";
        if (text.SpaceAfterMm is decimal after)
            css += "margin-bottom:" + Num(after) + "mm;";
        return css;
    }

    private static void AppendRun(StringBuilder html, TextRun run, ReportDefinition definition, DataScope scope, CompileOptions options)
    {
        var body = string.IsNullOrWhiteSpace(run.Path)
            ? Expand(run.Text, definition, scope, options)
            : Encode(ResolveText(run.Path, run.Format, definition, scope, options));
        html.Append("<span");
        if (run.Bold || run.Italic || run.Underline)
        {
            html.Append(" style=\"");
            if (run.Bold)
                html.Append("font-weight:700;");
            if (run.Italic)
                html.Append("font-style:italic;");
            if (run.Underline)
                html.Append("text-decoration:underline;");
            html.Append('"');
        }

        html.Append('>').Append(body).Append("</span>");
    }

    private static string ImageSource(ImageBlock image, DataScope scope)
    {
        if (string.IsNullOrWhiteSpace(image.SourcePath))
            return image.Source;
        if (!scope.TryGet(image.SourcePath, out var value) || value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString() ?? "";
    }

    private static string TextStyle(decimal fontSizePt, bool bold, bool italic, bool underline, string? fontFamily, decimal? lineHeight, string? color, TextAlign align, BoxStyle? box)
    {
        var css = new StringBuilder();
        css.Append(BoxCss(box));
        css.Append("font-size:").Append(Num(fontSizePt)).Append("pt;text-align:").Append(AlignCss(align)).Append(';');
        if (bold || box?.Bold == true)
            css.Append("font-weight:700;");
        if (italic || box?.Italic == true)
            css.Append("font-style:italic;");
        if (underline || box?.Underline == true)
            css.Append("text-decoration:underline;");
        var family = fontFamily ?? box?.FontFamily;
        if (family is not null)
            css.Append("font-family:").Append(StyleRules.FontStack(family)).Append(';');
        if (lineHeight is decimal leading)
            css.Append("line-height:").Append(Num(leading)).Append(';');
        else if (box?.LineHeight is decimal boxLeading)
            css.Append("line-height:").Append(Num(boxLeading)).Append(';');
        var ink = StyleRules.IsHexColor(color) ? color : box?.Color;
        if (StyleRules.IsHexColor(ink))
            css.Append("color:").Append(ink).Append(';');
        css.Append("white-space:pre-wrap;");
        return css.ToString();
    }

    private static string BoxCss(BoxStyle? box)
    {
        if (box is null)
            return "";

        var css = new StringBuilder();
        if (box.FontSizePt is decimal size)
            css.Append("font-size:").Append(Num(size)).Append("pt;");
        if (box.Bold == true)
            css.Append("font-weight:700;");
        if (box.Italic)
            css.Append("font-style:italic;");
        if (box.Underline)
            css.Append("text-decoration:underline;");
        if (box.FontFamily is not null)
            css.Append("font-family:").Append(StyleRules.FontStack(box.FontFamily)).Append(';');
        if (box.LineHeight is decimal leading)
            css.Append("line-height:").Append(Num(leading)).Append(';');
        if (StyleRules.IsHexColor(box.Color))
            css.Append("color:").Append(box.Color).Append(';');
        if (box.Margin is Spacing margin)
            css.Append("margin:").Append(Num(margin.Top)).Append("mm ").Append(Num(margin.Right)).Append("mm ")
                .Append(Num(margin.Bottom)).Append("mm ").Append(Num(margin.Left)).Append("mm;");
        if (box.Padding is Spacing padding)
            css.Append("padding:").Append(Num(padding.Top)).Append("mm ").Append(Num(padding.Right)).Append("mm ")
                .Append(Num(padding.Bottom)).Append("mm ").Append(Num(padding.Left)).Append("mm;");
        if (box.Border is { WidthMm: > 0 } border)
        {
            var color = StyleRules.IsHexColor(border.Color) ? border.Color : "#111827";
            var edge = Num(border.WidthMm) + "mm solid " + color;
            if (!border.HasChosenSide)
                css.Append("border:").Append(edge).Append(';');
            else
            {
                if (border.Top)
                    css.Append("border-top:").Append(edge).Append(';');
                if (border.Right)
                    css.Append("border-right:").Append(edge).Append(';');
                if (border.Bottom)
                    css.Append("border-bottom:").Append(edge).Append(';');
                if (border.Left)
                    css.Append("border-left:").Append(edge).Append(';');
            }
        }

        if (StyleRules.IsHexColor(box.Background))
            css.Append("background:").Append(box.Background).Append(';');
        return css.ToString();
    }

    private static string RuleStyle(RuleBlock rule)
    {
        var color = StyleRules.IsHexColor(rule.Color) ? rule.Color : "#111827";
        var css = new StringBuilder();
        css.Append(BoxCss(rule.Style));
        css.Append("border:0;border-top:").Append(Num(rule.ThicknessMm)).Append("mm solid ").Append(color)
            .Append(";height:0;margin:").Append(Num(rule.MarginTopMm)).Append("mm 0 ").Append(Num(rule.MarginBottomMm)).Append("mm;");
        return css.ToString();
    }

    private static string TableCss(TableStyle? style)
    {
        if (style is null)
            return "";

        var css = new StringBuilder();
        if (style.FontFamily is not null)
            css.Append("font-family:").Append(StyleRules.FontStack(style.FontFamily)).Append(';');
        if (style.FontSizePt is decimal size)
            css.Append("font-size:").Append(Num(size)).Append("pt;");
        if (style.Italic)
            css.Append("font-style:italic;");
        if (StyleRules.IsHexColor(style.Color))
            css.Append("color:").Append(style.Color).Append(';');
        if (StyleRules.IsHexColor(style.Background))
            css.Append("background:").Append(style.Background).Append(';');
        return css.ToString();
    }

    private static string CellCss(TableStyle? style, bool header)
    {
        if (style is null)
            return "";

        var css = new StringBuilder();
        if (style.CellPaddingMm is decimal padding)
            css.Append("padding:").Append(Num(padding)).Append("mm;");
        if (style.BorderWidthMm is decimal width)
        {
            if (width <= 0)
                css.Append("border:0;");
            else
            {
                var color = StyleRules.IsHexColor(style.BorderColor) ? style.BorderColor : "#111827";
                css.Append("border:").Append(Num(width)).Append("mm solid ").Append(color).Append(';');
            }
        }

        if (header && StyleRules.IsHexColor(style.HeaderBackground))
            css.Append("background:").Append(style.HeaderBackground).Append(';');
        return css.ToString();
    }

    private static string ColumnGrid(ColumnsBlock columns)
    {
        var css = new StringBuilder();
        css.Append("grid-template-columns:");
        for (var index = 0; index < columns.Columns.Count; index++)
        {
            if (index > 0)
                css.Append(' ');
            var column = columns.Columns[index];
            if (column.WidthMm is decimal width)
                css.Append(Num(width)).Append("mm");
            else if (column.Weight is decimal weight)
                css.Append(Num(weight)).Append("fr");
            else
                css.Append("minmax(0,1fr)");
        }

        css.Append(";align-items:start;");
        if (columns.GapMm is decimal gap)
            css.Append("gap:").Append(Num(gap)).Append("mm;");
        return css.ToString();
    }

    private static void AppendStyleAttribute(StringBuilder html, string css)
    {
        if (css.Length == 0)
            return;
        html.Append(" style=\"").Append(css).Append('"');
    }
}
