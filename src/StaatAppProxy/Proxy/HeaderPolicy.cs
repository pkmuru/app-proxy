using StaatAppProxy.Config;

namespace StaatAppProxy.Proxy;

/// <summary>
/// Decides which of the caller's headers are allowed to reach a backend.
/// </summary>
/// <remarks>
/// This is an allowlist rather than a denylist, deliberately. A backend should see an ordinary
/// service-to-service call, not a forwarded browser request: cookies, Origin, Referer, Sec-Fetch-*,
/// sec-ch-ua-* and the browser's own User-Agent all describe the caller, and several WAFs reject
/// traffic carrying them. Naming what may cross means the next header a client starts sending
/// cannot leak by accident — with a denylist it would.
/// </remarks>
public sealed class HeaderPolicy
{
    /// <summary>Enough for content negotiation and nothing else. Widen via Forwarding:AllowedHeaders.</summary>
    private static readonly string[] Defaults = ["Accept", "Content-Type"];

    private readonly HashSet<string> _allowed;

    public HeaderPolicy(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Forwarding:AllowedHeaders").Get<string[]>();

        _allowed = new HashSet<string>(
            configured is { Length: > 0 } ? configured : Defaults,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The global allowlist, shown in the admin UI so the trimming is not a mystery.</summary>
    public IReadOnlyCollection<string> Allowed => _allowed;

    public bool Allows(string header, ProxyEndpoint endpoint) =>
        !HopByHopHeaders.NeverForward(header) &&
        (_allowed.Contains(header) ||
         endpoint.ForwardHeaders.Contains(header, StringComparer.OrdinalIgnoreCase));
}
