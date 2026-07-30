using Microsoft.Identity.Client;
using StaatAppProxy.Auth;
using StaatAppProxy.Config;

namespace StaatAppProxy.Api;

/// <summary>
/// A token workbench for the admin UI: exchange a user's token for a backend one, or mint an
/// app-only token, without having to call a proxied route first.
/// </summary>
/// <remarks>
/// These answer 200 with <c>ok: false</c> rather than a 4xx when Entra ID refuses. The refusal
/// detail is the entire point of the endpoint, and most HTTP clients discard the body of an error
/// response. Unauthenticated like the rest of this service, and it hands back real tokens — another
/// reason to keep this off the public internet.
/// </remarks>
public static class TokenEndpoints
{
    public static void MapTokenEndpoints(this WebApplication app)
    {
        // Exchange a caller's access token for one a backend will accept. Name a configured
        // endpoint to borrow its oboScopes, or pass scopes directly.
        app.MapPost("/admin/api/tokens/obo", async (
            TokenRequest? body,
            OboTokenService obo,
            IEndpointConfigProvider config,
            CancellationToken cancellationToken) =>
        {
            var assertion = StripBearer(body?.Token);
            if (assertion is null)
            {
                return Results.Ok(TokenResult.Failed(
                    "No token supplied",
                    "Paste the user's access token — the one a client would send to this proxy."));
            }

            var (scopes, failure) = ResolveScopes(body, config);
            if (failure is not null)
            {
                return Results.Ok(failure);
            }

            return Results.Ok(await AcquireAsync(() => obo.AcquireAsync(assertion, scopes, cancellationToken)));
        });

        // No user involved: the proxy's own identity.
        app.MapPost("/admin/api/tokens/app", async (
            TokenRequest? body,
            OboTokenService obo,
            IEndpointConfigProvider config,
            CancellationToken cancellationToken) =>
        {
            var (scopes, failure) = ResolveScopes(body, config);
            if (failure is not null)
            {
                return Results.Ok(failure);
            }

            return Results.Ok(await AcquireAsync(() => obo.AcquireForAppAsync(scopes, cancellationToken)));
        });
    }

    /// <summary>Scopes named outright, or borrowed from a configured endpoint.</summary>
    private static (string[] Scopes, TokenResult? Failure) ResolveScopes(
        TokenRequest? body, IEndpointConfigProvider config)
    {
        var named = body?.Scopes?
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .ToArray() ?? [];

        if (named.Length > 0)
        {
            return (named, null);
        }

        if (string.IsNullOrWhiteSpace(body?.Endpoint))
        {
            return ([], TokenResult.Failed(
                "No scopes supplied",
                "Pick a configured endpoint, or name a scope such as \"api://<backend>/.default\"."));
        }

        var endpoint = config.Current.FirstOrDefault(
            candidate => string.Equals(candidate.Name, body.Endpoint, StringComparison.OrdinalIgnoreCase));

        if (endpoint is null)
        {
            return ([], TokenResult.Failed(
                $"No endpoint named \"{body.Endpoint}\"",
                "It may have been renamed or removed since this page was loaded. Reload to see the current list."));
        }

        if (endpoint.OboScopes.Length == 0)
        {
            return ([], TokenResult.Failed(
                $"Endpoint \"{endpoint.Name}\" has no scopes",
                $"Its auth mode is \"{endpoint.Auth}\", so it does not exchange tokens. Add \"oboScopes\" to it, "
                + "or name a scope here directly."));
        }

        return (endpoint.OboScopes, null);
    }

    private static async Task<TokenResult> AcquireAsync(Func<Task<AuthenticationResult>> acquire)
    {
        try
        {
            var result = await acquire();

            return new TokenResult
            {
                Ok = true,
                AccessToken = result.AccessToken,
                ExpiresOn = result.ExpiresOn,
                Scopes = result.Scopes.ToArray(),
                // "Cache" here means MSAL answered without calling Entra ID at all.
                TokenSource = result.AuthenticationResultMetadata.TokenSource.ToString(),
            };
        }
        catch (MsalServiceException ex)
        {
            // Entra ID answered and said no. ResponseBody carries the AADSTS code and trace ids
            // support will ask for, so it is passed through untouched.
            return TokenResult.Failed(ex.ErrorCode, ex.Message) with
            {
                CorrelationId = ex.CorrelationId,
                Response = ex.ResponseBody,
                Hint = Hint(ex.Message),
            };
        }
        catch (MsalException ex)
        {
            return TokenResult.Failed(ex.ErrorCode, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return TokenResult.Failed("Not configured", ex.Message);
        }
    }

    /// <summary>Turns the AADSTS code buried in Entra ID's message into the thing to go and fix.</summary>
    private static string? Hint(string message) => message switch
    {
        _ when message.Contains("AADSTS65001") =>
            "Nobody has consented to this proxy calling that API. Grant admin consent for the backend's "
            + "scope on the proxy's app registration — once per tenant, so a working dev tenant proves nothing "
            + "about prod.",

        _ when message.Contains("AADSTS50013") || message.Contains("AADSTS500133") =>
            "The token being exchanged is not one this proxy can use: its \"aud\" must be the proxy's own "
            + "client id, and it must not have expired. Check the claims above.",

        _ when message.Contains("AADSTS7000215") =>
            "AzureAd:ClientSecret is wrong or expired. Portal secrets show the value only once — check you "
            + "stored the value and not the secret id.",

        _ when message.Contains("AADSTS500011") || message.Contains("AADSTS650057") =>
            "The scope names an application this tenant does not know. Use the backend's Application ID URI "
            + "exactly as the portal shows it, e.g. \"api://<guid>/.default\".",

        _ when message.Contains("AADSTS50076") || message.Contains("AADSTS50079") =>
            "Conditional Access wants MFA before this backend may be called. The user's original token has to "
            + "be acquired with MFA already satisfied — the proxy cannot prompt on their behalf.",

        _ when message.Contains("AADSTS90002") =>
            "AzureAd:TenantId does not name a tenant Entra ID recognises.",

        _ => null,
    };

    /// <summary>Tolerates a token pasted straight out of a header, prefix and all.</summary>
    private static string? StripBearer(string? token)
    {
        var trimmed = token?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        const string prefix = "Bearer ";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..].Trim()
            : trimmed;
    }
}

/// <param name="Token">The user assertion to exchange. Ignored by the app-only endpoint.</param>
/// <param name="Scopes">Scopes to ask for, or empty to take them from <paramref name="Endpoint"/>.</param>
/// <param name="Endpoint">Name of a configured endpoint whose <c>oboScopes</c> to reuse.</param>
public sealed record TokenRequest(string? Token, string[]? Scopes, string? Endpoint);

public sealed record TokenResult
{
    public required bool Ok { get; init; }

    public string? AccessToken { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string[]? Scopes { get; init; }
    public string? TokenSource { get; init; }

    public string? Error { get; init; }
    public string? Detail { get; init; }
    public string? CorrelationId { get; init; }
    /// <summary>Entra ID's raw response, when it gave one.</summary>
    public string? Response { get; init; }
    /// <summary>What to do about it, for the error codes that come up often.</summary>
    public string? Hint { get; init; }

    public static TokenResult Failed(string error, string detail) =>
        new() { Ok = false, Error = error, Detail = detail };
}
