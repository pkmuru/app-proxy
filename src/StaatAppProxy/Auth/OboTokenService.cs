using Microsoft.Identity.Client;

namespace StaatAppProxy.Auth;

/// <summary>
/// Exchanges the caller's access token for one the backend will accept, using the On-Behalf-Of
/// flow. MSAL keeps its own in-memory cache keyed by user and scopes, so repeat calls do not go
/// back to Entra ID until the token is close to expiry.
/// </summary>
public sealed class OboTokenService
{
    /// <summary>The client MSAL is given, so outbound TLS settings apply to Entra ID calls too.</summary>
    public const string HttpClientName = "entra";

    // Neutral wording: the same client backs both the On-Behalf-Of exchange and app-only tokens.
    private const string NotConfigured =
        "Entra ID is not configured. Set AzureAd:TenantId, AzureAd:ClientId and AzureAd:ClientSecret.";

    private readonly IConfidentialClientApplication? _client;
    private readonly ILogger<OboTokenService> _log;

    public OboTokenService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<OboTokenService> log)
    {
        _log = log;

        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];
        var clientSecret = configuration["AzureAd:ClientSecret"];
        var instance = configuration["AzureAd:Instance"];
        if (string.IsNullOrWhiteSpace(instance))
        {
            instance = "https://login.microsoftonline.com";
        }

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            // Not an error on its own: plenty of setups only use passthrough endpoints.
            _log.LogWarning("{Message} Endpoints with auth \"obo\" will fail until it is.", NotConfigured);
            return;
        }

        _client = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority($"{instance.TrimEnd('/')}/{tenantId}")
            .WithHttpClientFactory(new MsalClientFactory(httpClientFactory))
            .Build();
    }

    /// <summary>Hands MSAL a client from the shared factory rather than letting it build its own.</summary>
    private sealed class MsalClientFactory(IHttpClientFactory factory) : IMsalHttpClientFactory
    {
        public HttpClient GetHttpClient() => factory.CreateClient(HttpClientName);
    }

    public bool IsConfigured => _client is not null;

    /// <param name="userAssertion">The caller's raw bearer token.</param>
    /// <exception cref="InvalidOperationException">Entra ID credentials are missing.</exception>
    /// <exception cref="MsalException">Entra ID refused the exchange.</exception>
    public async Task<AuthenticationResult> AcquireAsync(
        string userAssertion, string[] scopes, CancellationToken cancellationToken)
    {
        var result = await Client
            .AcquireTokenOnBehalfOf(scopes, new UserAssertion(userAssertion))
            .ExecuteAsync(cancellationToken);

        _log.LogDebug(
            "Acquired On-Behalf-Of token for {Scopes} from {Source}",
            string.Join(" ", scopes),
            result.AuthenticationResultMetadata.TokenSource);

        return result;
    }

    /// <summary>
    /// A token for the proxy's own identity, with no user involved. Not usable as an On-Behalf-Of
    /// assertion; it exists so the diagnostics UI can prove the client id and secret are right
    /// before an exchange failure gets blamed on them.
    /// </summary>
    /// <exception cref="InvalidOperationException">Entra ID credentials are missing.</exception>
    /// <exception cref="MsalException">Entra ID refused the request.</exception>
    public async Task<AuthenticationResult> AcquireForAppAsync(string[] scopes, CancellationToken cancellationToken)
    {
        var result = await Client.AcquireTokenForClient(scopes).ExecuteAsync(cancellationToken);

        _log.LogDebug("Acquired app-only token for {Scopes}", string.Join(" ", scopes));

        return result;
    }

    private IConfidentialClientApplication Client =>
        _client ?? throw new InvalidOperationException(NotConfigured);
}
