namespace ReportCreator.Model;

public sealed class ReportDefinition
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = "Report";

    public PageSetup Page { get; set; } = new();

    public Band Header { get; set; } = new();

    public Band Body { get; set; } = new();

    public Band Footer { get; set; } = new();

    public Band PageHeader { get; set; } = new();

    public Band PageFooter { get; set; } = new();
}

public sealed class PageSetup
{
    public PaperKind Paper { get; set; } = PaperKind.A4;

    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    public decimal? CustomWidthMm { get; set; }

    public decimal? CustomHeightMm { get; set; }

    public Margin Margin { get; set; } = new();

    public decimal PageHeaderMm { get; set; }

    public decimal PageFooterMm { get; set; }

    public PageNumberPlacement PageNumbers { get; set; } = PageNumberPlacement.None;
}

public sealed class Margin
{
    public decimal Top { get; set; } = 12;

    public decimal Right { get; set; } = 12;

    public decimal Bottom { get; set; } = 12;

    public decimal Left { get; set; } = 12;
}

public sealed class Band
{
    public List<Block> Blocks { get; set; } = [];
}
