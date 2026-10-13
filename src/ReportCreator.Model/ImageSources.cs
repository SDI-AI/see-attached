namespace ReportCreator.Model;

public static class ImageSources
{
    public const int MaxDataUriLength = 2_000_000;

    private static readonly string[] DataPrefixes =
    [
        "data:image/png;base64,",
        "data:image/jpeg;base64,",
        "data:image/gif;base64,",
        "data:image/webp;base64,"
    ];

    public static bool IsAllowed(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(source, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && !source.Contains('"', StringComparison.Ordinal)
                && !source.Contains('<', StringComparison.Ordinal)
                && !source.Contains('>', StringComparison.Ordinal)
                && !source.Contains('\\', StringComparison.Ordinal);
        }

        foreach (var prefix in DataPrefixes)
        {
            if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var payload = source[prefix.Length..];
            if (payload.Length == 0 || source.Length > MaxDataUriLength)
                return false;

            foreach (var character in payload)
            {
                if (char.IsAsciiLetterOrDigit(character) || character is '+' or '/' or '=' or '\r' or '\n')
                    continue;
                return false;
            }

            return true;
        }

        return false;
    }
}
