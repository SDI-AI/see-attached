namespace ReportCreator.Model;

public enum ShowWhenKind
{
    NotEmpty,
    Empty,
    Equals,
    NotEquals,
    GreaterThan,
    LessThan
}

public sealed class ShowWhen
{
    public string Path { get; set; } = "";

    public ShowWhenKind Kind { get; set; } = ShowWhenKind.NotEmpty;

    public string? Value { get; set; }
}
