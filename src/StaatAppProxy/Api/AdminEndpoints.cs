using StaatAppProxy.Auth;
using StaatAppProxy.Config;
using StaatAppProxy.Diagnostics;

namespace StaatAppProxy.Api;

/// <summary>
/// Read-only diagnostics behind the admin UI. Deliberately unauthenticated, like the rest of this
/// service — it exposes captured tokens, so keep it off the public internet.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/admin/api/config", (IEndpointConfigProvider config, OboTokenService obo) => Results.Ok(new
        {
            sourcePath = config.SourcePath,
            oboConfigured = obo.IsConfigured,
            endpoints = config.Current,
        }));

        app.MapGet("/admin/api/traffic", (TrafficStore traffic) =>
            Results.Ok(traffic.GetAll().Select(TrafficSummary.From)));

        app.MapGet("/admin/api/traffic/{id:guid}", (Guid id, TrafficStore traffic) =>
            traffic.Get(id) is { } exchange ? Results.Ok(exchange) : Results.NotFound());

        app.MapDelete("/admin/api/traffic", (TrafficStore traffic) =>
        {
            traffic.Clear();
            return Results.NoContent();
        });
    }
}
