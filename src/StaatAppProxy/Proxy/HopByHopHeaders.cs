namespace StaatAppProxy.Proxy;

/// <summary>
/// Headers that describe a single connection rather than the message, and so must not be copied
/// across the proxy.
/// </summary>
public static class HopByHopHeaders
{
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
    };

    /// <summary>
    /// Content-Length is dropped because the proxy rebuilds every body, and Accept-Encoding
    /// because the outbound handler negotiates compression itself — that is what keeps captured
    /// response bodies readable instead of gzipped.
    /// </summary>
    public static bool SkipInRequest(string name) =>
        HopByHop.Contains(name) ||
        Is(name, "Host") ||
        Is(name, "Content-Length") ||
        Is(name, "Accept-Encoding");

    /// <summary>
    /// Headers the proxy owns outright, whichever way it is configured: Authorization is decided
    /// by the endpoint's auth mode and User-Agent identifies this service, so letting a caller's
    /// value through either would defeat the point. Config validation rejects an attempt to
    /// forward one of these rather than ignoring it silently.
    /// </summary>
    public static bool NeverForward(string name) =>
        SkipInRequest(name) ||
        Is(name, "Authorization") ||
        Is(name, "User-Agent");

    /// <summary>
    /// Content-Encoding and Content-Length are dropped because responses arrive already
    /// decompressed and Kestrel re-frames them on the way out. The backend's CORS headers are
    /// dropped too: the proxy answers to the browser, so its own CORS policy is the only one that
    /// applies — copying the backend's as well would leave the browser with two conflicting
    /// Access-Control-Allow-Origin values and it would reject the response.
    /// </summary>
    public static bool SkipInResponse(string name) =>
        HopByHop.Contains(name) ||
        Is(name, "Content-Length") ||
        Is(name, "Content-Encoding") ||
        name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase);

    private static bool Is(string name, string other) => name.Equals(other, StringComparison.OrdinalIgnoreCase);
}
