using System.Text;

namespace StaatAppProxy.Diagnostics;

/// <summary>Turns a captured body into something worth showing in the diagnostics UI.</summary>
public static class BodyText
{
    public const int MaxBytes = 64 * 1024;

    private const string TruncationMarker = "…[truncated]";

    private static readonly string[] TextualHints =
        ["text/", "json", "xml", "javascript", "urlencoded", "html", "event-stream", "yaml", "csv", "graphql"];

    /// <summary>
    /// Decodes up to <see cref="MaxBytes"/> as UTF-8 text. Binary payloads are summarised rather
    /// than mangled, and anything longer is cut with a visible marker.
    /// </summary>
    public static string? Format(byte[]? body, string? contentType)
    {
        if (body is null || body.Length == 0)
        {
            return null;
        }

        if (!IsTextual(contentType))
        {
            return $"[binary: {contentType}, {body.Length} bytes]";
        }

        var take = Math.Min(body.Length, MaxBytes);
        var text = Encoding.UTF8.GetString(body, 0, take);
        return take < body.Length ? text + TruncationMarker : text;
    }

    /// <summary>A missing content type is treated as text — an unlabelled body is usually JSON.</summary>
    private static bool IsTextual(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ||
        TextualHints.Any(hint => contentType.Contains(hint, StringComparison.OrdinalIgnoreCase));
}
