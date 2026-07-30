using System.Diagnostics;
using System.Net;
using System.Net.Security;
using Azure.Core.Pipeline;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using StaatAppProxy.Api;
using StaatAppProxy.Auth;
using StaatAppProxy.Config;
using StaatAppProxy.Diagnostics;
using StaatAppProxy.Proxy;

const string CorsPolicy = "proxy-cors";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

// Browser clients call this service directly, so it terminates CORS itself. Backend CORS headers
// are stripped on the way back (see HopByHopHeaders) so the browser only ever sees this policy.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
{
    policy.AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("*");

    if (allowedOrigins.Length == 0)
    {
        // Nothing configured: this service is unauthenticated and internal by design, so any
        // origin may call it. List origins in Cors:AllowedOrigins to lock it down.
        policy.AllowAnyOrigin();
    }
    else
    {
        // A concrete list also permits cookie and Authorization credentials, which the
        // any-origin form is not allowed to do.
        policy.WithOrigins(allowedOrigins).AllowCredentials();
    }
}));
builder.Services.AddSingleton<IEndpointConfigProvider, FileEndpointConfigProvider>();
builder.Services.AddSingleton<HeaderPolicy>();
builder.Services.AddSingleton<TrafficStore>();
builder.Services.AddSingleton<OboTokenService>();
builder.Services.AddSingleton<Forwarder>();

builder.Services
    .AddHttpClient(Forwarder.HttpClientName)
    // Timeouts are per endpoint and applied per request, so the client itself must not impose one.
    .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // A redirect is the caller's decision to make, not the proxy's.
        AllowAutoRedirect = false,
        // Cookies belong to the caller's session and must not pool in a shared handler.
        UseCookies = false,
        // Decompress here so captured bodies are readable rather than gzipped bytes.
        AutomaticDecompression = DecompressionMethods.All,
        // Suppresses the traceparent/tracestate headers .NET would otherwise add to every
        // forwarded call. They announce that this request is one hop in a larger trace, which is
        // exactly what backends must not be told. Scoped to this handler, so incoming requests are
        // still correlated normally in Application Insights.
        ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator(),
        SslOptions = AcceptAnyCertificate(),
    });

// MSAL builds its own HttpClient unless handed one, so it needs the same treatment or token
// acquisition keeps failing while proxied calls succeed.
builder.Services
    .AddHttpClient(OboTokenService.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        SslOptions = AcceptAnyCertificate(),
    });

// Optional: with no connection string the app runs with no Azure dependency at all.
var applicationInsights = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(applicationInsights))
{
    builder.Services
        .AddOpenTelemetry()
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = applicationInsights;

            // The exporter has its own pipeline rather than an HttpClient from the factory.
            options.Transport = new HttpClientTransport(
                new HttpClient(new SocketsHttpHandler { SslOptions = AcceptAnyCertificate() }));
        });

    // Azure Monitor's HttpClient instrumentation injects traceparent itself, which the handler's
    // own ActivityHeadersPropagator does not cover — so backends would still be told they are one
    // hop in a larger trace. This turns W3C context propagation off process-wide. Traces within
    // this service are unaffected; what is lost is stitching them to a caller's or a backend's.
    Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator([]));
}

var app = builder.Build();

// Load the endpoint config now, so a broken file fails at startup instead of on the first request.
_ = app.Services.GetRequiredService<IEndpointConfigProvider>();

app.UseExceptionHandler();

// Ahead of the proxy, so browser preflight requests are answered here instead of being
// forwarded to a backend that knows nothing about this proxy's origins.
app.UseCors(CorsPolicy);

// Ahead of the static files, so a configured route always beats anything sitting in wwwroot.
app.UseMiddleware<ProxyMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapAdminEndpoints();
app.MapTokenEndpoints();
app.MapEchoEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDevSseEndpoints();
}

// Anything left over is a client-side route in the admin UI.
app.MapFallback((IWebHostEnvironment environment) =>
{
    var index = Path.Combine(environment.WebRootPath ?? "", "index.html");

    return File.Exists(index)
        ? Results.File(index, "text/html")
        // A fresh clone has no built UI yet. Say so, rather than returning a bare 404.
        : Results.Problem(
            title: "Admin UI has not been built",
            detail: "Run `npm install && npm run build` in the ui/ folder, then reload. "
                    + "The proxy and its APIs work either way.",
            statusCode: StatusCodes.Status404NotFound);
});

app.Run();

/// <summary>
/// Accepts any server certificate on every outbound call — backends, Entra ID and telemetry alike.
/// TLS-inspecting proxies re-sign each connection with a root this host does not trust, which
/// otherwise fails with an "UntrustedRoot" inner exception. Connections stay encrypted; what is
/// given up is the check that the other end is who it claims to be.
/// </summary>
static SslClientAuthenticationOptions AcceptAnyCertificate() => new()
{
    RemoteCertificateValidationCallback = (_, _, _, _) => true,
};
