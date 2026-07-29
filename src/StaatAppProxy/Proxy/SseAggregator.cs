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
    /// <summary>
    /// Takes the stream already read into memory rather than reading it here. Aggregation cannot
    /// answer until the last event has arrived anyway, and holding the raw text lets the traffic
    /// view show the original events beside the JSON they were folded into.
    /// </summary>
    public static string Aggregate(string stream, string sseMode, string concatField)
    {
        var events = ReadEvents(stream);
        return sseMode == SseModes.Concat ? Concatenated(events, concatField) : AsArray(events);
    }

    private static List<SseEvent> ReadEvents(string stream)
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

        using var reader = new StringReader(stream);
        while (reader.ReadLine() is { } line)
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
    /// <remarks>
    /// With <paramref name="field"/> set, each event is read as a JSON object and only that
    /// property contributes — the usual token-streaming shape, where the text arrives wrapped in
    /// an envelope nobody wants concatenated. Events without the property, a trailing "done" for
    /// instance, add nothing. If no event carries it at all the raw payloads are joined instead,
    /// so a field name that does not match shows the stream rather than an empty result.
    /// </remarks>
    private static string Concatenated(List<SseEvent> events, string field)
    {
        var payloads = events.Select(sseEvent => Payload(sseEvent.Data, field)).ToList();

        var joined = payloads.Any(payload => payload.Length > 0)
            ? string.Concat(payloads)
            : string.Concat(events.Select(sseEvent => sseEvent.Data));

        return TryParseJson(joined)?.ToJsonString()
               ?? new JsonObject { ["result"] = joined }.ToJsonString();
    }

    private static string Payload(string data, string field)
    {
        if (field.Length == 0)
        {
            return data;
        }

        if (TryParseJson(data) is not JsonObject json ||
            !json.TryGetPropertyValue(field, out var value) ||
            value is null)
        {
            return "";
        }

        // A string contributes its text; anything else its JSON, so nothing is quietly lost.
        return value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
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
