using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StaatAppProxy.Diagnostics;

namespace StaatAppProxy.Api;

/// <summary>
/// Replies with everything the proxy received. Nothing is masked — not even Authorization — so
/// that a caller can see exactly what arrived. That is the whole point of the endpoint, and the
/// reason this service belongs on an internal network only.
/// </summary>
public static class EchoEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void MapEchoEndpoints(this WebApplication app)
    {
        app.Map("/echo", HandleAsync);
        app.Map("/echo/{**path}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(HttpContext context, TrafficStore traffic)
    {
        var stopwatch = Stopwatch.StartNew();

        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        var body = buffer.ToArray();

        var payload = new EchoPayload(
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Request.QueryString.Value ?? "",
            context.Request.Query.ToDictionary(q => q.Key, q => string.Join(", ", q.Value.ToArray())),
            HeaderMap.From(context.Request.Headers),
            context.Request.ContentType,
            context.Request.ContentLength,
            context.Request.Protocol,
            context.Request.Scheme,
            context.Request.Host.Value ?? "",
            context.Connection.RemoteIpAddress?.ToString(),
            BodyText.Format(body, context.Request.ContentType),
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        stopwatch.Stop();

        traffic.Add(new CapturedExchange
        {
            Id = Guid.NewGuid(),
            Timestamp = payload.ReceivedAtUtc,
            Method = payload.Method,
            Path = payload.Path + payload.QueryString,
            EndpointName = "echo",
            Caller = TokenPeek.Caller(context.Request.Headers.Authorization),
            StatusCode = StatusCodes.Status200OK,
            DurationMs = stopwatch.ElapsedMilliseconds,
            RequestHeaders = payload.Headers,
            RequestBody = payload.Body,
            ResponseHeaders = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            ResponseBody = json,
        });

        return Results.Text(json, "application/json", Encoding.UTF8);
    }

    private sealed record EchoPayload(
        string Method,
        string Path,
        string QueryString,
        Dictionary<string, string> Query,
        Dictionary<string, string> Headers,
        string? ContentType,
        long? ContentLength,
        string Protocol,
        string Scheme,
        string Host,
        string? RemoteIp,
        string? Body,
        DateTimeOffset ReceivedAtUtc);
}
