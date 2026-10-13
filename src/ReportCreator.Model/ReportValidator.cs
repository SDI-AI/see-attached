using System.Globalization;

namespace ReportCreator.Model;

public static class ReportValidator
{
    public static IReadOnlyList<string> Validate(ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();

        if (definition.SchemaVersion != ReportDefinition.CurrentSchemaVersion)
            errors.Add($"Schema version {definition.SchemaVersion} is not supported. Use version {ReportDefinition.CurrentSchemaVersion}.");

        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Trim().Length > 120)
            errors.Add("The report name is required and must be 120 characters or fewer.");

        if (definition.Page is null)
            errors.Add("Page setup is required.");
        else
            ValidatePage(definition.Page, errors);

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

        var ids = new HashSet<string>(StringComparer.Ordinal);
        ValidateBand(definition.Header, "Header", errors, ids, insideColumns: false);
        ValidateBand(definition.Body, "Body", errors, ids, insideColumns: false);
        ValidateBand(definition.Footer, "Footer", errors, ids, insideColumns: false);
        ValidateBand(definition.PageHeader, "Page header", errors, ids, insideColumns: false);
        ValidateBand(definition.PageFooter, "Page footer", errors, ids, insideColumns: false);
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

    private static void ValidateBand(Band band, string scope, List<string> errors, HashSet<string> ids, bool insideColumns)
    {
        foreach (var block in band.Blocks)
            ValidateBlock(block, scope, errors, ids, insideColumns);
    }

    private static void ValidateBlock(Block block, string scope, List<string> errors, HashSet<string> ids, bool insideColumns)
    {
        if (string.IsNullOrWhiteSpace(block.Id) || block.Id.Length > 64 || block.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            errors.Add($"{scope} has a block whose id is missing or not a short letter, digit, '_' or '-' id.");
        else if (!ids.Add(block.Id))
            errors.Add($"Block id '{block.Id}' is used more than once.");

        var where = $"{scope} block '{block.Id}'";
        if (block.When is { } when)
        {
            if (!StyleRules.IsBindingPath(when.Path))
                errors.Add($"{where} show-when needs a binding path such as customer.name.");
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
                ValidateStyle(where, text.FontSizePt, text.Color, errors);
                break;
            case FieldBlock field:
                if (!StyleRules.IsBindingPath(field.Path))
                    errors.Add($"{where} needs a binding path such as customer.name.");
                ValidateStyle(where, field.FontSizePt, field.Color, errors);
                ValidateFormat(where, field.Format, errors);
                break;
            case ImageBlock image:
                if (!ImageSources.IsAllowed(image.Source))
                    errors.Add($"{where} image source must be an https URL or a png, jpeg, gif, or webp data URI.");
                RequireMm(image.WidthMm, $"{where} image width", errors, positive: true, allowNull: true);
                RequireMm(image.HeightMm, $"{where} image height", errors, positive: true, allowNull: true);
                if (image.Alt.Length > 200)
                    errors.Add($"{where} alt text is too long.");
                break;
            case SpacerBlock spacer:
                RequireMm(spacer.HeightMm, $"{where} spacer height", errors, positive: true);
                break;
            case ColumnsBlock columns:
                if (insideColumns)
                    errors.Add($"{where} cannot contain another columns block.");
                if (columns.Columns.Count is not 2 and not 3)
                    errors.Add($"{where} must have two or three columns.");
                foreach (var column in columns.Columns)
                {
                    foreach (var child in column.Blocks)
                        ValidateBlock(child, scope, errors, ids, insideColumns: true);
                }
                break;
            case TableBlock table:
                if (!StyleRules.IsBindingPath(table.DataPath))
                    errors.Add($"{where} needs a data path for its rows.");
                if (!string.IsNullOrWhiteSpace(table.GroupBy) && !StyleRules.IsBindingPath(table.GroupBy))
                    errors.Add($"{where} group path must look like customer.name.");
                if (table.Columns.Count == 0)
                    errors.Add($"{where} needs at least one column.");
                table.GroupAggregates ??= [];
                foreach (var column in table.Columns)
                {
                    if (string.IsNullOrWhiteSpace(column.Header) || column.Header.Length > 200)
                        errors.Add($"{where} has a column without a header.");
                    if (!StyleRules.IsBindingPath(column.Path))
                        errors.Add($"{where} has a column without a binding path.");
                    ValidateFormat(where, column.Format, errors);
                    RequireMm(column.WidthMm, $"{where} column width", errors, positive: true, allowNull: true);
                }

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

    private static void ValidateStyle(string where, decimal fontSizePt, string? color, List<string> errors)
    {
        if (fontSizePt is < 6 or > 96 || !StyleRules.HasAtMostTwoDecimalPlaces(fontSizePt))
            errors.Add($"{where} font size must be between 6 and 96 points.");
        if (color is not null && !StyleRules.IsHexColor(color))
            errors.Add($"{where} color must be a #RGB or #RRGGBB value.");
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
}
