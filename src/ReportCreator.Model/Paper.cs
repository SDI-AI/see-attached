namespace ReportCreator.Model;

public enum PaperKind
{
    A3,
    A4,
    A5,
    Letter,
    Legal,
    Tabloid,
    Custom
}

public enum PageOrientation
{
    Portrait,
    Landscape
}

public readonly record struct PageSize(decimal WidthMm, decimal HeightMm);

public static class PaperCatalog
{
    public static PageSize Portrait(PaperKind paper, decimal customWidthMm = 0, decimal customHeightMm = 0) => paper switch
    {
        PaperKind.A3 => new(297m, 420m),
        PaperKind.A4 => new(210m, 297m),
        PaperKind.A5 => new(148m, 210m),
        PaperKind.Letter => new(215.9m, 279.4m),
        PaperKind.Legal => new(215.9m, 355.6m),
        PaperKind.Tabloid => new(279.4m, 431.8m),
        PaperKind.Custom => new(customWidthMm, customHeightMm),
        _ => throw new ArgumentOutOfRangeException(nameof(paper))
    };

    public static PageSize Resolve(PageSetup page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var size = Portrait(page.Paper, page.CustomWidthMm ?? 0, page.CustomHeightMm ?? 0);
        return page.Orientation == PageOrientation.Landscape
            ? new PageSize(size.HeightMm, size.WidthMm)
            : size;
    }
}
