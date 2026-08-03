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

    /// <summary>
    /// The backend half of the exchange, for the traffic view. Null when the call never happened —
    /// a missing bearer token on an "obo" endpoint, for instance, is refused before that point.
    /// </summary>
    public BackendTrace? Backend { get; init; }

    /// <summary>
    /// The exception behind <see cref="Error"/> as text. Carried so the traffic view can show what
    /// actually failed — the message alone omits the cause, which sits in an inner exception.
    /// </summary>
    public string? Exception { get; init; }

    public string? ContentType =>
        Headers.TryGetValue("Content-Type", out var values) ? values.FirstOrDefault() : null;

    /// <summary>An RFC 7807 error the proxy produced itself, rather than something a backend said.</summary>
    public static ProxyResponse Problem(
        int status, string title, string detail, string? targetUrl = null, Exception? exception = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new ProblemDetails { Status = status, Title = title, Detail = detail },
            JsonOptions);

        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["application/problem+json; charset=utf-8"],
        };

        return new ProxyResponse(status, headers, body, targetUrl, $"{title}: {detail}")
        {
            Exception = exception?.ToString(),
        };
    }
}

/// <summary>
/// What the proxy actually sent to a backend and what came back, before any transformation.
/// </summary>
/// <remarks>
/// Recorded separately from the client-facing pair because the two differ in ways that matter when
/// something is wrong: request headers are trimmed to an allowlist and given this service's own
/// identity, and an SSE response is folded into JSON. Without this, the traffic view would show
/// what was asked for and what was answered, but not what happened in between.
/// </remarks>
public sealed record BackendTrace
{
    public required string Method { get; init; }
    public required string Url { get; init; }

    public required Dictionary<string, string[]> RequestHeaders { get; init; }
    public required byte[] RequestBody { get; init; }
    public string? RequestContentType { get; init; }

    // Absent when the backend never answered: unreachable, TLS refused, or timed out. The request
    // half above is still recorded, since what was sent is exactly what has to be examined then.
    public int? StatusCode { get; init; }
    public Dictionary<string, string[]>? ResponseHeaders { get; init; }

    /// <summary>Untransformed: the raw event stream when the endpoint aggregates SSE.</summary>
    public byte[]? ResponseBody { get; init; }
    public string? ResponseContentType { get; init; }

    /// <summary>Why no response came back, when none did.</summary>
    public string? Error { get; init; }
}
