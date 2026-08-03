namespace StaatAppProxy.Api;

/// <summary>
/// Stand-in SSE backends, mapped only in Development, so both aggregation modes can be exercised
/// without a real streaming service. Point an endpoint at these in endpoints.json to try them.
/// </summary>
public static class DevSseEndpoints
{
    public static void MapDevSseEndpoints(this WebApplication app)
    {
        // Discrete events — the shape "array" mode is built for.
        app.MapGet("/dev/sse-sample", async (HttpContext context) =>
        {
            StartEventStream(context);

            for (var seq = 1; seq <= 3; seq++)
            {
                await WriteEventAsync(context, "message", $$"""{"seq":{{seq}},"text":"chunk-{{seq}}"}""");
                await Task.Delay(200, context.RequestAborted);
            }

            await WriteEventAsync(context, "done", """{"finished":true}""");
        });

        // Token-at-a-time text in an envelope — the shape "concat" mode is built for.
        app.MapGet("/dev/sse-tokens", async (HttpContext context) =>
        {
            StartEventStream(context);

            foreach (var token in new[] { "Hello", ", ", "streaming", " ", "world", "!" })
            {
                await WriteEventAsync(context, "message", $$"""{"value":"{{token}}","seq":1}""");
                await Task.Delay(100, context.RequestAborted);
            }

            // No "value", so concat mode contributes nothing from it.
            await WriteEventAsync(context, "done", """{"finished":true}""");
        });

        // Events that say what they are — the shape "typed" mode is built for. Progress events
        // surround the answer, and the suggestions arrive together at the end.
        app.MapGet("/dev/sse-typed", async (HttpContext context) =>
        {
            StartEventStream(context);

            await WriteEventAsync(context, "message", """{"key":"answer","type":"start","value":null}""");
            await WriteEventAsync(context, "message", """{"key":"answer","type":"status","value":"thinking"}""");

            foreach (var token in new[] { "Hello", ", ", "typed", " ", "world", "!" })
            {
                await WriteEventAsync(
                    context, "message", $$"""{"key":"answer","type":"streaming","value":"{{token}}"}""");

                await Task.Delay(100, context.RequestAborted);
            }

            await WriteEventAsync(
                context, "message", """{"key":"suggest","type":"followup","value":["Tell me more","Why?"]}""");

            await WriteEventAsync(context, "message", """{"key":"answer","type":"end","value":null}""");
        });
    }

    private static void StartEventStream(HttpContext context)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
    }

    private static async Task WriteEventAsync(HttpContext context, string name, string data)
    {
        await context.Response.WriteAsync($"event: {name}\ndata: {data}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
}
