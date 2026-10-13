using System.Text.Json.Serialization;

namespace ReportCreator.Model;

public enum TextAlign
{
    Left,
    Center,
    Right
}

public enum AggregateKind
{
    Sum,
    Count,
    Avg
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(FieldBlock), "field")]
[JsonDerivedType(typeof(ImageBlock), "image")]
[JsonDerivedType(typeof(SpacerBlock), "spacer")]
[JsonDerivedType(typeof(ColumnsBlock), "columns")]
[JsonDerivedType(typeof(TableBlock), "table")]
public abstract class Block
{
    public string Id { get; set; } = "";

    public bool KeepTogether { get; set; }

    public bool PageBreakBefore { get; set; }

    public bool PageBreakAfter { get; set; }

    public ShowWhen? When { get; set; }
}

public sealed class TextBlock : Block
{
    public string Text { get; set; } = "";

    public decimal FontSizePt { get; set; } = 11;

    public bool Bold { get; set; }

    public string? Color { get; set; }

    public TextAlign Align { get; set; } = TextAlign.Left;
}

public sealed class FieldBlock : Block
{
    public string Path { get; set; } = "";

    public string? Format { get; set; }

    public decimal FontSizePt { get; set; } = 11;

    public bool Bold { get; set; }

    public string? Color { get; set; }

    public TextAlign Align { get; set; } = TextAlign.Left;
}

public sealed class ImageBlock : Block
{
    public string Source { get; set; } = "";

    public decimal? WidthMm { get; set; }

    public decimal? HeightMm { get; set; }

    public string Alt { get; set; } = "";
}

public sealed class SpacerBlock : Block
{
    public decimal HeightMm { get; set; } = 6;
}

public sealed class Column
{
    public List<Block> Blocks { get; set; } = [];
}

public sealed class ColumnsBlock : Block
{
    public List<Column> Columns { get; set; } = [];
}

public sealed class TableColumn
{
    public string Header { get; set; } = "";

    public string Path { get; set; } = "";

    public string? Format { get; set; }

    public TextAlign Align { get; set; } = TextAlign.Left;

    public decimal? WidthMm { get; set; }

    public bool RunningTotal { get; set; }
}

public sealed class TableAggregate
{
    public string? Label { get; set; }

    public string ColumnPath { get; set; } = "";

    public AggregateKind Kind { get; set; } = AggregateKind.Sum;

    public string? Format { get; set; }
}

public sealed class TableBlock : Block
{
    public string DataPath { get; set; } = "";

    public bool ShowHeader { get; set; } = true;

    public string? GroupBy { get; set; }

    public bool BreakBeforeGroup { get; set; }

    public bool KeepGroupTogether { get; set; }

    public List<TableColumn> Columns { get; set; } = [];

    public List<TableAggregate> Aggregates { get; set; } = [];

    public List<TableAggregate> GroupAggregates { get; set; } = [];
}
