using StaatAppProxy.Auth;
using StaatAppProxy.Config;
using StaatAppProxy.Diagnostics;
using StaatAppProxy.Proxy;

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

        app.MapGet("/admin/api/config", (IEndpointConfigProvider config, OboTokenService obo, HeaderPolicy headers) =>
            Results.Ok(new
            {
                sourcePath = config.SourcePath,
                oboConfigured = obo.IsConfigured,
                allowedHeaders = headers.Allowed,
                endpoints = config.Current,
            }));

        // Lets the admin UI sign a user in and obtain a real access token to test "obo" endpoints
        // with. Served from here so there is one place to configure Entra ID, rather than a
        // separate build-time .env for the UI. No secret is involved: a SPA is a public client.
        app.MapGet("/admin/api/client-auth", (IConfiguration configuration) =>
        {
            var tenantId = configuration["ClientAuth:TenantId"];
            var clientId = configuration["ClientAuth:ClientId"];
            var scopes = configuration.GetSection("ClientAuth:Scopes").Get<string[]>() ?? [];

            var instance = configuration["ClientAuth:Instance"];
            if (string.IsNullOrWhiteSpace(instance))
            {
                instance = "https://login.microsoftonline.com";
            }

            var enabled = !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(clientId);

            return Results.Ok(new
            {
                enabled,
                clientId = clientId ?? "",
                authority = enabled ? $"{instance.TrimEnd('/')}/{tenantId}" : "",
                scopes,
            });
        });

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
