namespace StaatAppProxy.Config;

/// <summary>
/// One configurable backend route. Loaded from the endpoint config file (JSON or YAML) and
/// reloaded whenever that file changes.
/// </summary>
public sealed record ProxyEndpoint
{
    /// <summary>Unique, human readable id. Shown in the UI and against captured traffic.</summary>
    public string Name { get; init; } = "";

    /// <summary>Incoming path prefix that selects this endpoint, e.g. "/api/orders".</summary>
    public string RoutePrefix { get; init; } = "";

    /// <summary>Absolute base URL of the backend, e.g. "https://orders.internal".</summary>
    public string BackendBaseUrl { get; init; } = "";

    /// <summary>"rest" passes the backend response through; "sse" aggregates an event stream into JSON.</summary>
    public string Mode { get; init; } = ProxyModes.Rest;

    /// <summary>How SSE events are combined: "array", "concat" or "typed". Ignored unless <see cref="Mode"/> is "sse".</summary>
    public string SseMode { get; init; } = SseModes.Array;

    /// <summary>
    /// In "concat" mode, the JSON property to take from each event rather than the whole payload —
    /// the text inside a token-streaming envelope. Set to "" to join raw payloads instead.
    /// </summary>
    public string SseConcatField { get; init; } = "value";

    /// <summary>"none", "passthrough" or "obo".</summary>
    public string Auth { get; init; } = AuthModes.None;

    /// <summary>Scopes requested for the On-Behalf-Of exchange. Required when <see cref="Auth"/> is "obo".</summary>
    public string[] OboScopes { get; init; } = [];

    /// <summary>
    /// Extra caller headers this endpoint lets through, on top of the global allowlist in
    /// <c>Forwarding:AllowedHeaders</c>. Use for application headers a backend genuinely needs,
    /// e.g. "X-Correlation-Id".
    /// </summary>
    public string[] ForwardHeaders { get; init; } = [];

    /// <summary>
    /// Fixed headers added to every backend call for this endpoint, e.g. an API key the caller
    /// never sees. These win over anything else, so they can also pin a specific User-Agent.
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Budget for the whole backend call, including reading an SSE stream to completion.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Set to false to take an endpoint out of service without deleting it.</summary>
    public bool Enabled { get; init; } = true;
}

public static class ProxyModes
{
    public const string Rest = "rest";
    public const string Sse = "sse";

    public static readonly string[] All = [Rest, Sse];
}

public static class SseModes
{
    /// <summary>Every event becomes an entry in a JSON array: <c>[{ "event": ..., "data": ... }]</c>.</summary>
    public const string Array = "array";

    /// <summary>All data payloads are joined into one value, for backends that stream chunks of a single result.</summary>
    public const string Concat = "concat";

    /// <summary>Only the "streaming" and "followup" events of a stream whose events name their own kind.</summary>
    public const string Typed = "typed";

    public static readonly string[] All = [Array, Concat, Typed];
}

public static class AuthModes
{
    /// <summary>Strip the caller's Authorization header.</summary>
    public const string None = "none";

    /// <summary>Forward the caller's Authorization header untouched.</summary>
    public const string Passthrough = "passthrough";

    /// <summary>Exchange the caller's token for a backend token via the On-Behalf-Of flow.</summary>
    public const string Obo = "obo";

    public static readonly string[] All = [None, Passthrough, Obo];
}
