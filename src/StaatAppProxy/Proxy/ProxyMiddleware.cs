using System.Diagnostics;
using Microsoft.Extensions.Primitives;
using StaatAppProxy.Config;
using StaatAppProxy.Diagnostics;

namespace StaatAppProxy.Proxy;

/// <summary>
/// Picks the configured endpoint that matches the incoming path, forwards the request and records
/// the exchange. Requests that match nothing carry on to the static files and the admin API.
/// </summary>
public sealed class ProxyMiddleware(
    RequestDelegate next,
    IEndpointConfigProvider endpoints,
    Forwarder forwarder,
    TrafficStore traffic,
    ILogger<ProxyMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = Match(context.Request.Path, endpoints.Current);
        if (endpoint is null)
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var requestBody = Array.Empty<byte>();
        ProxyResponse result;

        try
        {
            requestBody = await ReadBodyAsync(context.Request, context.RequestAborted);
            result = await forwarder.ForwardAsync(context, endpoint, requestBody, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            log.LogDebug("Client aborted {Method} {Path}", context.Request.Method, context.Request.Path);
            return;
        }
        catch (Exception ex)
        {
            // Recorded rather than rethrown so the failure still shows up in the diagnostics UI.
            log.LogError(ex, "Unhandled error proxying {Method} {Path} to '{Endpoint}'",
                context.Request.Method, context.Request.Path, endpoint.Name);

            result = ProxyResponse.Problem(
                StatusCodes.Status500InternalServerError, "Proxy error", ex.Message, exception: ex);
        }

        stopwatch.Stop();

        try
        {
            await WriteAsync(context, result);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            log.LogDebug("Client aborted before the response to {Path} could be written", context.Request.Path);
        }

        // Built only once the response has gone out, so nothing in here may throw: the client
        // already holds its body, and an exception at this point truncates that response mid-flight
        // and drops the row — a failure that is invisible from both ends.
        try
        {
            traffic.Add(new CapturedExchange
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Method = context.Request.Method,
                Path = context.Request.Path + context.Request.QueryString,
                EndpointName = endpoint.Name,
                Caller = TokenPeek.Caller(context.Request.Headers.Authorization),
                TargetUrl = result.TargetUrl,
                StatusCode = result.StatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                RequestHeaders = HeaderMap.From(context.Request.Headers),
                RequestBody = BodyText.Format(requestBody, context.Request.ContentType),
                ResponseHeaders = HeaderMap.From(result.Headers),
                ResponseBody = BodyText.Format(result.Body, result.ContentType),
                BackendRequestHeaders = result.Backend is null ? null : HeaderMap.From(result.Backend.RequestHeaders),
                BackendRequestBody = result.Backend is null
                    ? null
                    : BodyText.Format(result.Backend.RequestBody, result.Backend.RequestContentType),
                BackendStatusCode = result.Backend?.StatusCode,
                BackendResponseHeaders = result.Backend?.ResponseHeaders is { } backendHeaders
                    ? HeaderMap.From(backendHeaders)
                    : null,
                BackendResponseBody = result.Backend?.ResponseBody is { } backendBody
                    ? BodyText.Format(backendBody, result.Backend.ResponseContentType)
                    : null,
                BackendError = result.Backend?.Error,
                Error = result.Error,
                Exception = result.Exception,
            });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not record {Method} {Path} in the traffic view",
                context.Request.Method, context.Request.Path);

            // The exchange itself happened and the client was served; only the record of it failed.
            // A row saying so beats the request disappearing from the view with the reason left
            // behind in the console.
            traffic.Add(new CapturedExchange
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Method = context.Request.Method,
                Path = context.Request.Path + context.Request.QueryString,
                EndpointName = endpoint.Name,
                StatusCode = result.StatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                RequestHeaders = new Dictionary<string, string>(),
                ResponseHeaders = new Dictionary<string, string>(),
                Error = "This exchange completed, but the proxy could not record it in full.",
                Exception = ex.ToString(),
            });
        }

        log.LogInformation("{Method} {Path} -> '{Endpoint}' {Status} in {Duration}ms",
            context.Request.Method, context.Request.Path, endpoint.Name, result.StatusCode, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// The longest matching prefix wins, so "/api/orders/archive" can be routed somewhere other
    /// than "/api/orders". Matching is on whole segments: "/api/orders" never matches "/api/ordersx".
    /// </summary>
    private static ProxyEndpoint? Match(PathString path, IReadOnlyList<ProxyEndpoint> endpoints)
    {
        ProxyEndpoint? best = null;

        foreach (var candidate in endpoints)
        {
            if (!candidate.Enabled || !path.StartsWithSegments(candidate.RoutePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (best is null || candidate.RoutePrefix.Length > best.RoutePrefix.Length)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static async Task WriteAsync(HttpContext context, ProxyResponse result)
    {
        context.Response.StatusCode = result.StatusCode;

        foreach (var (name, values) in result.Headers)
        {
            context.Response.Headers[name] = new StringValues(values);
        }

        await context.Response.Body.WriteAsync(result.Body, context.RequestAborted);
    }
}
