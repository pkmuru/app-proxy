using System.Text;
using Microsoft.Identity.Client;
using StaatAppProxy.Auth;
using StaatAppProxy.Config;

namespace StaatAppProxy.Proxy;

/// <summary>
/// Sends one incoming request to its backend and brings the answer back, aggregating SSE streams
/// on the way through when the endpoint asks for it.
/// </summary>
/// <remarks>
/// Bodies are buffered in both directions so they can be recorded for the diagnostics UI. That is
/// the right trade for JSON APIs; this proxy is not meant to carry large file transfers.
/// </remarks>
public sealed class Forwarder(
    IHttpClientFactory httpClientFactory,
    OboTokenService oboTokens,
    HeaderPolicy headerPolicy,
    ILogger<Forwarder> log)
{
    public const string HttpClientName = "proxy";

    /// <summary>Identifies this service to backends, in place of whatever the caller's client sent.</summary>
    private static readonly string UserAgent =
        "StaatAppProxy/" + (typeof(Forwarder).Assembly.GetName().Version?.ToString(3) ?? "1.0");

    public async Task<ProxyResponse> ForwardAsync(
        HttpContext context,
        ProxyEndpoint endpoint,
        byte[] requestBody,
        CancellationToken cancellationToken)
    {
        var targetUrl = BuildTargetUrl(context.Request, endpoint);

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);
        if (requestBody.Length > 0)
        {
            request.Content = new ByteArrayContent(requestBody);
        }

        CopyRequestHeaders(context.Request, request, endpoint);
        ApplyIdentity(request, endpoint);

        if (endpoint.Mode == ProxyModes.Sse)
        {
            // Replace rather than add: the backend is being asked for a stream, whatever the
            // caller happened to say it accepts.
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("text/event-stream");
        }

        var authFailure = await ApplyAuthAsync(context.Request, endpoint, request, cancellationToken);
        if (authFailure is not null)
        {
            return authFailure with { TargetUrl = targetUrl };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(endpoint.TimeoutSeconds));

        var client = httpClientFactory.CreateClient(HttpClientName);

        // Snapshot before sending: once the handler owns the message its content may be disposed.
        var sentHeaders = Snapshot(request);

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            var headers = CopyResponseHeaders(response);
            var backendBody = await response.Content.ReadAsByteArrayAsync(timeout.Token);

            var trace = new BackendTrace
            {
                Method = request.Method.Method,
                Url = targetUrl,
                RequestHeaders = sentHeaders,
                RequestBody = requestBody,
                RequestContentType = request.Content?.Headers.ContentType?.ToString(),
                StatusCode = (int)response.StatusCode,
                ResponseHeaders = headers,
                ResponseBody = backendBody,
                ResponseContentType = response.Content.Headers.ContentType?.ToString(),
            };

            // A failed SSE call has no stream worth aggregating — relay whatever the backend said.
            if (endpoint.Mode == ProxyModes.Sse && response.IsSuccessStatusCode)
            {
                var aggregated = SseAggregator.Aggregate(Encoding.UTF8.GetString(backendBody), endpoint.SseMode);

                // Copied first: the aggregated response is JSON, but the backend's own headers are
                // kept in the trace above exactly as they arrived.
                var clientHeaders = new Dictionary<string, string[]>(headers, StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = ["application/json; charset=utf-8"],
                };

                return new ProxyResponse(
                    (int)response.StatusCode, clientHeaders, Encoding.UTF8.GetBytes(aggregated), targetUrl)
                {
                    Backend = trace,
                };
            }

            return new ProxyResponse((int)response.StatusCode, headers, backendBody, targetUrl) { Backend = trace };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning(
                "Endpoint '{Endpoint}' timed out after {Timeout}s calling {Target}",
                endpoint.Name, endpoint.TimeoutSeconds, targetUrl);

            return ProxyResponse.Problem(
                StatusCodes.Status504GatewayTimeout,
                "Backend timed out",
                $"'{endpoint.Name}' did not respond within {endpoint.TimeoutSeconds}s. Target: {targetUrl}",
                targetUrl);
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "Endpoint '{Endpoint}' could not be reached at {Target}", endpoint.Name, targetUrl);

            return ProxyResponse.Problem(
                StatusCodes.Status502BadGateway,
                "Backend unreachable",
                $"'{endpoint.Name}' could not be reached: {ex.Message} Target: {targetUrl}",
                targetUrl);
        }
    }

    /// <summary>Everything after the route prefix, plus the original query string, onto the backend base URL.</summary>
    private static string BuildTargetUrl(HttpRequest request, ProxyEndpoint endpoint)
    {
        // ToUriComponent keeps percent-encoding intact; Path.Value would hand back a decoded path.
        var path = request.Path.ToUriComponent();
        var suffix = path.Length > endpoint.RoutePrefix.Length ? path[endpoint.RoutePrefix.Length..] : "";
        return endpoint.BackendBaseUrl + suffix + request.QueryString.ToUriComponent();
    }

    /// <summary>
    /// Copies only what <see cref="HeaderPolicy"/> allows. Everything else the caller sent —
    /// cookies, Origin, Referer, the browser's User-Agent, X-Forwarded-* — is dropped, so the
    /// backend sees a request from this service rather than a relayed browser call.
    /// </summary>
    private void CopyRequestHeaders(HttpRequest incoming, HttpRequestMessage outgoing, ProxyEndpoint endpoint)
    {
        foreach (var (name, values) in incoming.Headers)
        {
            // A header the endpoint sets itself wins outright; copying the caller's as well would
            // send both values.
            if (!headerPolicy.Allows(name, endpoint) || endpoint.Headers.ContainsKey(name))
            {
                continue;
            }

            var array = values.ToArray();

            // Content headers live on the content, not the request; let HttpClient sort out which is which.
            if (!outgoing.Headers.TryAddWithoutValidation(name, array))
            {
                outgoing.Content?.Headers.TryAddWithoutValidation(name, array);
            }
        }
    }

    /// <summary>Applies the endpoint's own fixed headers, then names this service to the backend.</summary>
    /// <remarks>
    /// Nothing is removed here, deliberately: HttpHeaders.Remove throws when the name belongs to
    /// the other collection (User-Agent on content headers, Content-Type on request headers), and
    /// the copy above already skips anything this endpoint sets, so there is nothing to displace.
    /// </remarks>
    private static void ApplyIdentity(HttpRequestMessage outgoing, ProxyEndpoint endpoint)
    {
        foreach (var (name, value) in endpoint.Headers)
        {
            if (!outgoing.Headers.TryAddWithoutValidation(name, value))
            {
                outgoing.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Last, and only if the endpoint has not pinned one a fussy backend insists on.
        if (!outgoing.Headers.Contains("User-Agent"))
        {
            outgoing.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        }
    }

    /// <summary>The outgoing headers as they stand, for the traffic view.</summary>
    private static Dictionary<string, string[]> Snapshot(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, values) in request.Headers)
        {
            headers[name] = values.ToArray();
        }

        if (request.Content is not null)
        {
            foreach (var (name, values) in request.Content.Headers)
            {
                headers[name] = values.ToArray();
            }
        }

        return headers;
    }

    private static Dictionary<string, string[]> CopyResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            if (HopByHopHeaders.SkipInResponse(name))
            {
                continue;
            }

            // Kept as separate values because Set-Cookie must not be folded into one header.
            headers[name] = values.ToArray();
        }

        return headers;
    }

    /// <summary>Returns null when the request may proceed, or the response to send back instead.</summary>
    private async Task<ProxyResponse?> ApplyAuthAsync(
        HttpRequest incoming,
        ProxyEndpoint endpoint,
        HttpRequestMessage outgoing,
        CancellationToken cancellationToken)
    {
        var authorization = incoming.Headers.Authorization.ToString();

        switch (endpoint.Auth)
        {
            case AuthModes.Passthrough:
                if (!string.IsNullOrWhiteSpace(authorization))
                {
                    outgoing.Headers.TryAddWithoutValidation("Authorization", authorization);
                }

                return null;

            case AuthModes.Obo:
                var assertion = BearerToken(authorization);
                if (assertion is null)
                {
                    return ProxyResponse.Problem(
                        StatusCodes.Status401Unauthorized,
                        "Bearer token required",
                        $"Endpoint '{endpoint.Name}' exchanges the caller's token for a backend token, " +
                        "so an \"Authorization: Bearer <token>\" header is required.");
                }

                try
                {
                    var token = await oboTokens.AcquireAsync(assertion, endpoint.OboScopes, cancellationToken);
                    outgoing.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                    return null;
                }
                catch (MsalException ex)
                {
                    log.LogError(ex, "On-Behalf-Of exchange failed for endpoint '{Endpoint}'", endpoint.Name);

                    return ProxyResponse.Problem(
                        StatusCodes.Status502BadGateway,
                        "On-Behalf-Of exchange failed",
                        $"{ex.ErrorCode}: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    return ProxyResponse.Problem(
                        StatusCodes.Status502BadGateway,
                        "On-Behalf-Of not configured",
                        ex.Message);
                }

            default:
                return null;
        }
    }

    private static string? BearerToken(string? header)
    {
        const string prefix = "Bearer ";

        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}
