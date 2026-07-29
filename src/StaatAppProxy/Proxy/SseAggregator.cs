using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StaatAppProxy.Config;

namespace StaatAppProxy.Proxy;

/// <summary>
/// Reads a text/event-stream to completion and folds it into a single JSON document, so callers
/// that cannot consume SSE still get the whole result in one ordinary REST response.
/// </summary>
public static class SseAggregator
{
    public static async Task<string> AggregateAsync(Stream stream, string sseMode, CancellationToken cancellationToken)
    {
        var events = await ReadEventsAsync(stream, cancellationToken);
        return sseMode == SseModes.Concat ? Concatenated(events) : AsArray(events);
    }

    private static async Task<List<SseEvent>> ReadEventsAsync(Stream stream, CancellationToken cancellationToken)
    {
        var events = new List<SseEvent>();
        var data = new StringBuilder();
        string? eventName = null;
        var hasData = false;

        void Dispatch()
        {
            if (hasData || eventName is not null)
            {
                events.Add(new SseEvent(eventName ?? "message", data.ToString()));
            }

            eventName = null;
            hasData = false;
            data.Clear();
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                Dispatch();
                continue;
            }

            if (line[0] == ':')
            {
                // Comment line, usually a keep-alive ping.
                continue;
            }

            var separator = line.IndexOf(':');
            var field = separator < 0 ? line : line[..separator];
            var value = separator < 0 ? "" : line[(separator + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            switch (field)
            {
                case "event":
                    eventName = value;
                    break;

                case "data":
                    if (hasData)
                    {
                        data.Append('\n');
                    }

                    data.Append(value);
                    hasData = true;
                    break;

                // "id" and "retry" only matter to clients that reconnect, which this proxy never does.
            }
        }

        // Plenty of servers close the stream without a trailing blank line; keep that last event.
        Dispatch();
        return events;
    }

    /// <summary>One entry per event, for streams where each event is a separate result.</summary>
    private static string AsArray(List<SseEvent> events)
    {
        var array = new JsonArray();
        foreach (var sseEvent in events)
        {
            array.Add(new JsonObject
            {
                ["event"] = sseEvent.Name,
                ["data"] = TryParseJson(sseEvent.Data) ?? JsonValue.Create(sseEvent.Data),
            });
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// All payloads joined in order, for streams that deliver one logical result in chunks.
    /// A joined payload that is itself valid JSON is returned as-is rather than double-wrapped.
    /// </summary>
    private static string Concatenated(List<SseEvent> events)
    {
        var joined = string.Concat(events.Select(e => e.Data));
        return TryParseJson(joined)?.ToJsonString()
               ?? new JsonObject { ["result"] = joined }.ToJsonString();
    }

    private static JsonNode? TryParseJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record SseEvent(string Name, string Data);
}
