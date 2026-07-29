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
    ILogger<Forwarder> log)
{
    public const string HttpClientName = "proxy";

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

        CopyRequestHeaders(context.Request, request);

        if (endpoint.Mode == ProxyModes.Sse)
        {
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

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var headers = CopyResponseHeaders(response);

            // A failed SSE call has no stream worth aggregating — relay whatever the backend said.
            if (endpoint.Mode == ProxyModes.Sse && response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                var aggregated = await SseAggregator.AggregateAsync(stream, endpoint.SseMode, timeout.Token);

                headers["Content-Type"] = ["application/json; charset=utf-8"];
                return new ProxyResponse((int)response.StatusCode, headers, Encoding.UTF8.GetBytes(aggregated), targetUrl);
            }

            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return new ProxyResponse((int)response.StatusCode, headers, body, targetUrl);
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

    private static void CopyRequestHeaders(HttpRequest incoming, HttpRequestMessage outgoing)
    {
        foreach (var (name, values) in incoming.Headers)
        {
            // Authorization is owned by ApplyAuthAsync.
            if (HopByHopHeaders.SkipInRequest(name) || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
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
