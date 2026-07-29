using Microsoft.Identity.Client;

namespace StaatAppProxy.Auth;

/// <summary>
/// Exchanges the caller's access token for one the backend will accept, using the On-Behalf-Of
/// flow. MSAL keeps its own in-memory cache keyed by user and scopes, so repeat calls do not go
/// back to Entra ID until the token is close to expiry.
/// </summary>
public sealed class OboTokenService
{
    private const string NotConfigured =
        "On-Behalf-Of is not configured. Set AzureAd:TenantId, AzureAd:ClientId and AzureAd:ClientSecret.";

    private readonly IConfidentialClientApplication? _client;
    private readonly ILogger<OboTokenService> _log;

    public OboTokenService(IConfiguration configuration, ILogger<OboTokenService> log)
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
            .Build();
    }

    public bool IsConfigured => _client is not null;

    /// <param name="userAssertion">The caller's raw bearer token.</param>
    /// <exception cref="InvalidOperationException">Entra ID credentials are missing.</exception>
    /// <exception cref="MsalException">Entra ID refused the exchange.</exception>
    public async Task<string> AcquireAsync(string userAssertion, string[] scopes, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException(NotConfigured);
        }

        var result = await _client
            .AcquireTokenOnBehalfOf(scopes, new UserAssertion(userAssertion))
            .ExecuteAsync(cancellationToken);

        _log.LogDebug(
            "Acquired On-Behalf-Of token for {Scopes} from {Source}",
            string.Join(" ", scopes),
            result.AuthenticationResultMetadata.TokenSource);

        return result.AccessToken;
    }
}
