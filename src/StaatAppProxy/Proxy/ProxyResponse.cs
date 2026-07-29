using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace StaatAppProxy.Proxy;

/// <summary>
/// Everything the client will be sent. Proxy-generated errors use this shape too, so there is one
/// place that writes a response and one place that records it.
/// </summary>
public sealed record ProxyResponse(
    int StatusCode,
    Dictionary<string, string[]> Headers,
    byte[] Body,
    string? TargetUrl = null,
    string? Error = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? ContentType =>
        Headers.TryGetValue("Content-Type", out var values) ? values.FirstOrDefault() : null;

    /// <summary>An RFC 7807 error the proxy produced itself, rather than something a backend said.</summary>
    public static ProxyResponse Problem(int status, string title, string detail, string? targetUrl = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new ProblemDetails { Status = status, Title = title, Detail = detail },
            JsonOptions);

        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["application/problem+json; charset=utf-8"],
        };

        return new ProxyResponse(status, headers, body, targetUrl, $"{title}: {detail}");
    }
}
