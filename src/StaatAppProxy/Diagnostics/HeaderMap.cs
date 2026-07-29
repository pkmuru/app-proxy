namespace StaatAppProxy.Diagnostics;

/// <summary>Flattens header collections into something the UI can render directly.</summary>
public static class HeaderMap
{
    public static Dictionary<string, string> From(IHeaderDictionary headers) =>
        headers.ToDictionary(
            header => header.Key,
            header => string.Join(", ", header.Value.ToArray()),
            StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> From(IReadOnlyDictionary<string, string[]> headers) =>
        headers.ToDictionary(
            header => header.Key,
            header => string.Join(", ", header.Value),
            StringComparer.OrdinalIgnoreCase);
}
