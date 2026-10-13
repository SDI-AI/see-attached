namespace ReportCreator.Model;

public enum PageNumberPlacement
{
    None,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public static class PageMetrics
{
    public const decimal DefaultBandMm = 14;

    public const decimal NumberBandMm = 6;

    public static decimal Header(ReportDefinition definition)
    {
        if (definition.PageHeader?.Blocks is not { Count: > 0 })
            return 0;
        return definition.Page.PageHeaderMm > 0 ? definition.Page.PageHeaderMm : DefaultBandMm;
    }

    public static decimal Footer(ReportDefinition definition)
    {
        if (definition.PageFooter?.Blocks is not { Count: > 0 })
            return 0;
        return definition.Page.PageFooterMm > 0 ? definition.Page.PageFooterMm : DefaultBandMm;
    }

    public static decimal Numbers(PageSetup page) =>
        page.PageNumbers == PageNumberPlacement.None ? 0 : NumberBandMm;
}
