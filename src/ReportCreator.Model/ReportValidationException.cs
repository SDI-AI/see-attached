namespace ReportCreator.Model;

public sealed class ReportValidationException : Exception
{
    public ReportValidationException(IReadOnlyList<string> errors)
        : base(errors.Count == 0 ? "The report is not valid." : string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
