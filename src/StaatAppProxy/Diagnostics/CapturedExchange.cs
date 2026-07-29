namespace StaatAppProxy.Diagnostics;

/// <summary>
/// A request/response pair kept in memory for the diagnostics UI. Headers are stored exactly as
/// they were seen — nothing is masked, because the whole point is to see what really went over
/// the wire. Keep this service off the public internet.
/// </summary>
public sealed record CapturedExchange
{
    public required Guid Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Method { get; init; }

    /// <summary>Path and query string as the client sent them.</summary>
    public required string Path { get; init; }

    /// <summary>Name of the matched endpoint, "echo", or null when nothing matched.</summary>
    public string? EndpointName { get; init; }

    /// <summary>Absolute URL the request was forwarded to, if it was forwarded.</summary>
    public string? TargetUrl { get; init; }

    public required int StatusCode { get; init; }
    public required long DurationMs { get; init; }

    /// <summary>What the client sent to the proxy.</summary>
    public required IReadOnlyDictionary<string, string> RequestHeaders { get; init; }

    public string? RequestBody { get; init; }

    /// <summary>What the proxy sent back to the client, after any SSE aggregation.</summary>
    public required IReadOnlyDictionary<string, string> ResponseHeaders { get; init; }

    public string? ResponseBody { get; init; }

    /// <summary>
    /// What the proxy sent on to the backend, after headers were trimmed to the allowlist and the
    /// endpoint's own were applied. Null when the request never reached a backend.
    /// </summary>
    public IReadOnlyDictionary<string, string>? BackendRequestHeaders { get; init; }

    public string? BackendRequestBody { get; init; }

    /// <summary>What the backend answered, untransformed — the raw event stream for SSE routes.</summary>
    public int? BackendStatusCode { get; init; }

    public IReadOnlyDictionary<string, string>? BackendResponseHeaders { get; init; }

    public string? BackendResponseBody { get; init; }

    /// <summary>Set when the proxy itself failed, e.g. the backend was unreachable.</summary>
    public string? Error { get; init; }
}

/// <summary>Row shape for the traffic list, so the list response stays small.</summary>
public sealed record TrafficSummary(
    Guid Id,
    DateTimeOffset Timestamp,
    string Method,
    string Path,
    string? EndpointName,
    int StatusCode,
    long DurationMs,
    string? Error)
{
    public static TrafficSummary From(CapturedExchange exchange) => new(
        exchange.Id,
        exchange.Timestamp,
        exchange.Method,
        exchange.Path,
        exchange.EndpointName,
        exchange.StatusCode,
        exchange.DurationMs,
        exchange.Error);
}
