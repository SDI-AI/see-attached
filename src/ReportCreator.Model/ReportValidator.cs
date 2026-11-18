using System.Globalization;
using System.Text.RegularExpressions;

namespace ReportCreator.Model;

public static partial class ReportValidator
{
    public static IReadOnlyList<string> Validate(ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();

        if (definition.SchemaVersion < ReportDefinition.MinimumSchemaVersion || definition.SchemaVersion > ReportDefinition.CurrentSchemaVersion)
            errors.Add($"Schema version {definition.SchemaVersion} is not supported. Use version {ReportDefinition.CurrentSchemaVersion}.");

        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Trim().Length > 120)
            errors.Add("The report name is required and must be 120 characters or fewer.");

        if (definition.Page is null)
            errors.Add("Page setup is required.");
        else
            ValidatePage(definition.Page, errors);

        definition.Parameters ??= [];
        definition.Header ??= new Band();
        definition.Body ??= new Band();
        definition.Footer ??= new Band();
        definition.PageHeader ??= new Band();
        definition.PageFooter ??= new Band();
        definition.Header.Blocks ??= [];
        definition.Body.Blocks ??= [];
        definition.Footer.Blocks ??= [];
        definition.PageHeader.Blocks ??= [];
        definition.PageFooter.Blocks ??= [];

        var parameters = ValidateParameters(definition, errors);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        ValidateBand(definition.Header, "Header", errors, ids, parameters, columnDepth: 0, panelDepth: 0, repeatDepth: 0);
        ValidateBand(definition.Body, "Body", errors, ids, parameters, columnDepth: 0, panelDepth: 0, repeatDepth: 0);
        ValidateBand(definition.Footer, "Footer", errors, ids, parameters, columnDepth: 0, panelDepth: 0, repeatDepth: 0);
        ValidateBand(definition.PageHeader, "Page header", errors, ids, parameters, columnDepth: 0, panelDepth: 0, repeatDepth: 0);
        ValidateBand(definition.PageFooter, "Page footer", errors, ids, parameters, columnDepth: 0, panelDepth: 0, repeatDepth: 0);
        if (definition.Page is not null && definition.Page.Margin is not null && errors.All(error => !error.Contains("no room for content", StringComparison.Ordinal)))
            ValidateChromeRoom(definition, errors);
        return errors;
    }

    private static void ValidatePage(PageSetup page, List<string> errors)
    {
        if (!Enum.IsDefined(page.Paper))
            errors.Add("The paper size is not recognized.");
        if (!Enum.IsDefined(page.Orientation))
            errors.Add("The page orientation is not recognized.");

        if (page.Paper == PaperKind.Custom)
        {
            RequireMm(page.CustomWidthMm, "Custom page width", errors, positive: true);
            RequireMm(page.CustomHeightMm, "Custom page height", errors, positive: true);
        }

        if (page.Margin is null)
        {
            errors.Add("Page margins are required.");
            return;
        }

        RequireMm(page.Margin.Top, "Top margin", errors, positive: false);
        RequireMm(page.Margin.Right, "Right margin", errors, positive: false);
        RequireMm(page.Margin.Bottom, "Bottom margin", errors, positive: false);
        RequireMm(page.Margin.Left, "Left margin", errors, positive: false);
        RequireMm(page.PageHeaderMm, "Page header height", errors, positive: false);
        RequireMm(page.PageFooterMm, "Page footer height", errors, positive: false);
        if (!Enum.IsDefined(page.PageNumbers))
            errors.Add("The page number placement is not recognized.");
        if (page.Watermark is not null)
            ValidateWatermark(page.Watermark, errors);

        if (page.Paper == PaperKind.Custom && (page.CustomWidthMm is null or <= 0 || page.CustomHeightMm is null or <= 0))
            return;

        var size = PaperCatalog.Resolve(page);
        if (page.Margin.Left + page.Margin.Right >= size.WidthMm)
            errors.Add("The left and right margins leave no room for content.");
        if (page.Margin.Top + page.Margin.Bottom >= size.HeightMm)
            errors.Add("The top and bottom margins leave no room for content.");
    }

    private static void ValidateChromeRoom(ReportDefinition definition, List<string> errors)
    {
        var page = definition.Page;
        var size = PaperCatalog.Resolve(page);
        var used = page.Margin.Top + page.Margin.Bottom + PageMetrics.Header(definition) + PageMetrics.Footer(definition) + PageMetrics.Numbers(page);
        if (used >= size.HeightMm)
            errors.Add("The page header, page footer, page numbers, and margins leave no room for content.");
    }

    private static HashSet<string> ValidateParameters(ReportDefinition definition, List<string> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (definition.Parameters.Count > 32)
            errors.Add("A report can declare at most 32 parameters.");

        foreach (var parameter in definition.Parameters)
        {
            var label = string.IsNullOrWhiteSpace(parameter.Name) ? "(unnamed)" : parameter.Name.Trim();
            if (!ParameterRules.IsName(parameter.Name))
                errors.Add($"Parameter '{label}' needs a name made of letters, digits, and underscores.");
            else if (!names.Add(parameter.Name))
                errors.Add($"Parameter '{parameter.Name}' is declared more than once.");

            if (!Enum.IsDefined(parameter.Kind))
                errors.Add($"Parameter '{label}' must be a string, number, date, or bool.");
            else if (!ParameterRules.DefaultMatches(parameter, out var problem))
                errors.Add($"Parameter '{label}' default {problem}.");
        }

        return names;
    }

    private static void ValidateBand(Band band, string scope, List<string> errors, HashSet<string> ids, HashSet<string> parameters, int columnDepth, int panelDepth, int repeatDepth)
    {
        foreach (var block in band.Blocks)
            ValidateBlock(block, scope, errors, ids, parameters, columnDepth, panelDepth, repeatDepth);
    }

    private static void ValidateBlock(Block block, string scope, List<string> errors, HashSet<string> ids, HashSet<string> parameters, int columnDepth, int panelDepth, int repeatDepth)
    {
        if (string.IsNullOrWhiteSpace(block.Id) || block.Id.Length > 64 || block.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            errors.Add($"{scope} has a block whose id is missing or not a short letter, digit, '_' or '-' id.");
        else if (!ids.Add(block.Id))
            errors.Add($"Block id '{block.Id}' is used more than once.");

        var where = $"{scope} block '{block.Id}'";
        ValidateBox(where, block.Style, errors);
        if (block.When is { } when)
        {
            if (!StyleRules.IsBindingPath(when.Path))
                errors.Add($"{where} show-when needs a binding path such as customer.name.");
            CheckParameter(when.Path, where, parameters, errors);
            if (!Enum.IsDefined(when.Kind))
                errors.Add($"{where} show-when comparison is not recognized.");
            if (when.Kind is ShowWhenKind.Equals or ShowWhenKind.NotEquals or ShowWhenKind.GreaterThan or ShowWhenKind.LessThan
                && string.IsNullOrWhiteSpace(when.Value))
                errors.Add($"{where} show-when needs a comparison value.");
        }

        switch (block)
        {
            case TextBlock text:
                if (text.Text.Length > 20_000)
                    errors.Add($"{where} has too much text.");
                foreach (Match match in Token().Matches(text.Text))
                    CheckParameter(match.Groups[1].Value, where, parameters, errors);
                ValidateStyle(where, text.FontSizePt, text.Color, errors);
                ValidateFont(where, text.FontFamily, text.LineHeight, errors);
                RequireMm(text.SpaceBeforeMm, $"{where} space before", errors, positive: false, allowNull: true);
                RequireMm(text.SpaceAfterMm, $"{where} space after", errors, positive: false, allowNull: true);
                if (text.Runs is { Count: > 0 })
                {
                    if (text.Runs.Count > 40)
                        errors.Add($"{where} has too many text runs.");
                    foreach (var run in text.Runs)
                    {
                        if ((run.Text ?? "").Length > 4_000)
                            errors.Add($"{where} has a text run that is too long.");
                        if (!string.IsNullOrWhiteSpace(run.Path) && !StyleRules.IsBindingPath(run.Path))
                            errors.Add($"{where} has a text run with a bad binding path.");
                        CheckParameter(run.Path, where, parameters, errors);
                        ValidateFormat(where, run.Format, errors);
                        foreach (Match match in Token().Matches(run.Text ?? ""))
                            CheckParameter(match.Groups[1].Value, where, parameters, errors);
                    }
                }
                break;
            case FieldBlock field:
                if (!StyleRules.IsBindingPath(field.Path))
                    errors.Add($"{where} needs a binding path such as customer.name.");
                CheckParameter(field.Path, where, parameters, errors);
                ValidateStyle(where, field.FontSizePt, field.Color, errors);
                ValidateFont(where, field.FontFamily, field.LineHeight, errors);
                ValidateFormat(where, field.Format, errors);
                break;
            case RuleBlock rule:
                RequireMm(rule.ThicknessMm, $"{where} rule thickness", errors, positive: true);
                RequireMm(rule.MarginTopMm, $"{where} rule margin", errors, positive: false);
                RequireMm(rule.MarginBottomMm, $"{where} rule margin", errors, positive: false);
                if (rule.Color is not null && !StyleRules.IsHexColor(rule.Color))
                    errors.Add($"{where} color must be a #RGB or #RRGGBB value.");
                break;
            case PanelBlock panel:
                if (panelDepth >= 6)
                    errors.Add($"{where} is nested too deeply.");
                panel.Blocks ??= [];
                foreach (var child in panel.Blocks)
                    ValidateBlock(child, scope, errors, ids, parameters, columnDepth, panelDepth + 1, repeatDepth);
                break;
            case RepeatBlock repeat:
                if (repeatDepth >= 4)
                    errors.Add($"{where} is nested too deeply.");
                if (!StyleRules.IsBindingPath(repeat.DataPath))
                    errors.Add($"{where} needs a data path for its items.");
                repeat.Blocks ??= [];
                foreach (var child in repeat.Blocks)
                    ValidateBlock(child, scope, errors, ids, parameters, columnDepth, panelDepth, repeatDepth + 1);
                break;
            case ImageBlock image:
                var bound = !string.IsNullOrWhiteSpace(image.SourcePath);
                if (bound && !StyleRules.IsBindingPath(image.SourcePath))
                    errors.Add($"{where} image source path must look like item.logo.");
                CheckParameter(image.SourcePath, where, parameters, errors);
                if (!string.IsNullOrWhiteSpace(image.Source) && !ImageSources.IsAllowed(image.Source))
                    errors.Add($"{where} image source must be an https URL or a png, jpeg, gif, or webp data URI.");
                else if (!bound && !ImageSources.IsAllowed(image.Source))
                    errors.Add($"{where} image source must be an https URL or a png, jpeg, gif, or webp data URI.");
                if (!Enum.IsDefined(image.Fit))
                    errors.Add($"{where} image fit is not recognized.");
                if (!Enum.IsDefined(image.Align))
                    errors.Add($"{where} image alignment is not recognized.");
                RequireMm(image.WidthMm, $"{where} image width", errors, positive: true, allowNull: true);
                RequireMm(image.HeightMm, $"{where} image height", errors, positive: true, allowNull: true);
                if (image.Alt.Length > 200)
                    errors.Add($"{where} alt text is too long.");
                break;
            case SpacerBlock spacer:
                RequireMm(spacer.HeightMm, $"{where} spacer height", errors, positive: true);
                break;
            case ColumnsBlock columns:
                if (columnDepth >= 2)
                    errors.Add($"{where} cannot contain another columns block.");
                if (columns.Columns.Count is < 1 or > 8)
                    errors.Add($"{where} must have between one and eight columns.");
                RequireMm(columns.GapMm, $"{where} column gap", errors, positive: false, allowNull: true);
                foreach (var column in columns.Columns)
                {
                    column.Blocks ??= [];
                    RequireMm(column.WidthMm, $"{where} column width", errors, positive: true, allowNull: true);
                    if (column.Weight is decimal weight && (weight <= 0 || weight > 100 || !StyleRules.HasAtMostTwoDecimalPlaces(weight)))
                        errors.Add($"{where} column weight must be greater than zero.");
                    if (column.WidthMm is not null && column.Weight is not null)
                        errors.Add($"{where} column sets both a width and a weight.");
                    foreach (var child in column.Blocks)
                        ValidateBlock(child, scope, errors, ids, parameters, columnDepth + 1, panelDepth, repeatDepth);
                }
                break;
            case TableBlock table:
                if (!StyleRules.IsBindingPath(table.DataPath))
                    errors.Add($"{where} needs a data path for its rows.");
                if (!string.IsNullOrWhiteSpace(table.GroupBy) && !StyleRules.IsBindingPath(table.GroupBy))
                    errors.Add($"{where} group path must look like customer.name.");
                if (!string.IsNullOrWhiteSpace(table.GroupKey) && !StyleRules.IsBindingPath(table.GroupKey))
                    errors.Add($"{where} group key must look like customer.id.");
                CheckParameter(table.GroupBy, where, parameters, errors);
                CheckParameter(table.GroupKey, where, parameters, errors);
                if (table.Columns.Count == 0)
                    errors.Add($"{where} needs at least one column.");
                table.GroupAggregates ??= [];
                foreach (var column in table.Columns)
                {
                    if (string.IsNullOrWhiteSpace(column.Header) || column.Header.Length > 200)
                        errors.Add($"{where} has a column without a header.");
                    if (!StyleRules.IsBindingPath(column.Path))
                        errors.Add($"{where} has a column without a binding path.");
                    CheckParameter(column.Path, where, parameters, errors);
                    ValidateFormat(where, column.Format, errors);
                    RequireMm(column.WidthMm, $"{where} column width", errors, positive: true, allowNull: true);
                }

                ValidateTableStyle(where, table.TableStyle, errors);
                foreach (var aggregate in table.Aggregates.Concat(table.GroupAggregates))
                {
                    if (table.Columns.All(column => column.Path != aggregate.ColumnPath))
                        errors.Add($"{where} aggregate points at '{aggregate.ColumnPath}', which is not a column.");
                    ValidateFormat(where, aggregate.Format, errors);
                    if (aggregate.Label is { Length: > 80 })
                        errors.Add($"{where} aggregate label is too long.");
                }
                break;
        }
    }

    private static void CheckParameter(string? path, string where, HashSet<string> parameters, List<string> errors)
    {
        if (PrintPaths.TryParameterName(path, out var name) && !parameters.Contains(name))
            errors.Add($"{where} refers to parameter '{name}', which is not declared.");
    }

    private static void ValidateWatermark(PageWatermark watermark, List<string> errors)
    {
        if (!Enum.IsDefined(watermark.Kind))
            errors.Add("The watermark kind is not recognized.");
        if (!Enum.IsDefined(watermark.Horizontal))
            errors.Add("The watermark alignment is not recognized.");
        if (!Enum.IsDefined(watermark.Vertical))
            errors.Add("The watermark position is not recognized.");
        if (watermark.FontSizePt is < 6 or > PageWatermark.MaxFontSizePt || !StyleRules.HasAtMostTwoDecimalPlaces(watermark.FontSizePt))
            errors.Add($"Watermark font size must be between 6 and {PageWatermark.MaxFontSizePt:0} points.");
        if (watermark.Color is not null && !StyleRules.IsHexColor(watermark.Color))
            errors.Add("Watermark color must be a #RGB or #RRGGBB value.");
        if (watermark.FontFamily is not null && !StyleRules.IsFontFamily(watermark.FontFamily))
            errors.Add("Watermark font must be Source Sans 3, Arial, Segoe UI, Times New Roman, Georgia, or Courier New.");
        if (watermark.Opacity is decimal opacity && (opacity < 0 || opacity > 1 || !StyleRules.HasAtMostTwoDecimalPlaces(opacity)))
            errors.Add("Watermark opacity must be between 0 and 1.");
        if (watermark.RotationDegrees is < -180 or > 180 || !StyleRules.HasAtMostTwoDecimalPlaces(watermark.RotationDegrees))
            errors.Add("Watermark rotation must be between -180 and 180 degrees.");
        if (watermark.Text.Contains('\r') || watermark.Text.Contains('\n'))
            errors.Add("Watermark text must be one line.");
        else if (watermark.Text.Length > 80)
            errors.Add("Watermark text must be 80 characters or fewer.");
        else if (watermark.Enabled && watermark.Kind == WatermarkKind.Text && string.IsNullOrWhiteSpace(watermark.Text))
            errors.Add("Watermark text is required.");

        var sourceSet = !string.IsNullOrWhiteSpace(watermark.Source);
        if (watermark.Kind == WatermarkKind.Image && watermark.Enabled && !ImageSources.IsAllowed(watermark.Source))
            errors.Add("Watermark image source must be an https URL or a png, jpeg, gif, or webp data URI.");
        else if (sourceSet && !ImageSources.IsAllowed(watermark.Source))
            errors.Add("Watermark image source must be an https URL or a png, jpeg, gif, or webp data URI.");

        RequireMm(watermark.WidthMm, "Watermark image width", errors, positive: true, allowNull: true);
        RequireMm(watermark.HeightMm, "Watermark image height", errors, positive: true, allowNull: true);
    }

    private static void ValidateStyle(string where, decimal fontSizePt, string? color, List<string> errors)
    {
        if (fontSizePt is < 6 or > 96 || !StyleRules.HasAtMostTwoDecimalPlaces(fontSizePt))
            errors.Add($"{where} font size must be between 6 and 96 points.");
        if (color is not null && !StyleRules.IsHexColor(color))
            errors.Add($"{where} color must be a #RGB or #RRGGBB value.");
    }

    private static void ValidateFont(string where, string? family, decimal? lineHeight, List<string> errors)
    {
        if (family is not null && !StyleRules.IsFontFamily(family))
            errors.Add($"{where} font must be Source Sans 3, Arial, Segoe UI, Times New Roman, Georgia, or Courier New.");
        if (lineHeight is not null && (lineHeight is < 0.8m or > 3m || !StyleRules.HasAtMostTwoDecimalPlaces(lineHeight.Value)))
            errors.Add($"{where} line height must be between 0.8 and 3.");
    }

    private static void ValidateBox(string where, BoxStyle? style, List<string> errors)
    {
        if (style is null)
            return;

        ValidateFont(where, style.FontFamily, style.LineHeight, errors);
        if (style.FontSizePt is decimal size)
            ValidateStyle(where, size, style.Color, errors);
        else if (style.Color is not null && !StyleRules.IsHexColor(style.Color))
            errors.Add($"{where} color must be a #RGB or #RRGGBB value.");
        if (style.Background is not null && !StyleRules.IsHexColor(style.Background))
            errors.Add($"{where} background must be a #RGB or #RRGGBB value.");
        ValidateSpacing(where, "margin", style.Margin, errors);
        ValidateSpacing(where, "padding", style.Padding, errors);
        if (style.Border is { } border)
        {
            RequireMm(border.WidthMm, $"{where} border width", errors, positive: true);
            if (border.Color is not null && !StyleRules.IsHexColor(border.Color))
                errors.Add($"{where} border color must be a #RGB or #RRGGBB value.");
        }
    }

    private static void ValidateTableStyle(string where, TableStyle? style, List<string> errors)
    {
        if (style is null)
            return;

        ValidateFont(where, style.FontFamily, null, errors);
        if (style.FontSizePt is decimal size)
            ValidateStyle(where, size, style.Color, errors);
        else if (style.Color is not null && !StyleRules.IsHexColor(style.Color))
            errors.Add($"{where} color must be a #RGB or #RRGGBB value.");
        if (style.HeaderBackground is not null && !StyleRules.IsHexColor(style.HeaderBackground))
            errors.Add($"{where} header background must be a #RGB or #RRGGBB value.");
        if (style.Background is not null && !StyleRules.IsHexColor(style.Background))
            errors.Add($"{where} background must be a #RGB or #RRGGBB value.");
        RequireMm(style.CellPaddingMm, $"{where} cell padding", errors, positive: false, allowNull: true);
        RequireMm(style.BorderWidthMm, $"{where} border width", errors, positive: false, allowNull: true);
        if (style.BorderColor is not null && !StyleRules.IsHexColor(style.BorderColor))
            errors.Add($"{where} border color must be a #RGB or #RRGGBB value.");
    }

    private static void ValidateSpacing(string where, string name, Spacing? spacing, List<string> errors)
    {
        if (spacing is null)
            return;

        RequireMm(spacing.Top, $"{where} {name}", errors, positive: false);
        RequireMm(spacing.Right, $"{where} {name}", errors, positive: false);
        RequireMm(spacing.Bottom, $"{where} {name}", errors, positive: false);
        RequireMm(spacing.Left, $"{where} {name}", errors, positive: false);
    }

    private static void ValidateFormat(string where, string? format, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(format))
            return;
        if (format.Length > 40 || !IsUsableFormat(format))
            errors.Add($"{where} has a format string .NET cannot apply to a number or a date.");
    }

    private static bool IsUsableFormat(string format)
    {
        try
        {
            _ = 1.5m.ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
        }

        try
        {
            _ = new DateTime(2026, 1, 2).ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void RequireMm(decimal? value, string label, List<string> errors, bool positive, bool allowNull = false)
    {
        if (value is null)
        {
            if (!allowNull)
                errors.Add($"{label} is required.");
            return;
        }

        if (positive ? value <= 0 : value < 0)
            errors.Add(positive ? $"{label} must be greater than zero." : $"{label} cannot be negative.");
        else if (value > 2000 || !StyleRules.HasAtMostTwoDecimalPlaces(value.Value))
            errors.Add($"{label} must be in millimetres with at most two decimal places.");
    }

    [GeneratedRegex("\\{([A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*)(?::([^{}]+))?\\}")]
    private static partial Regex Token();
}
