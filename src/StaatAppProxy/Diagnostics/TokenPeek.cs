using System.Buffers.Text;
using System.Text.Json;

namespace StaatAppProxy.Diagnostics;

/// <summary>
/// Reads the payload of a bearer token far enough to name the caller. The signature is never
/// checked and nothing here is a security decision — it exists so the traffic list can say who a
/// request came from instead of showing a base64 blob. The token itself is captured untouched and
/// decoded properly in the browser.
/// </summary>
public static class TokenPeek
{
    /// <summary>Long enough for any real Entra ID token, short enough to bound a bad one.</summary>
    private const int MaxPayloadChars = 8 * 1024;

    /// <summary>First one present wins: the user if there is one, otherwise the calling app.</summary>
    private static readonly string[] CallerClaims =
        ["upn", "preferred_username", "unique_name", "email", "app_displayname", "appid", "azp"];

    /// <summary>
    /// Who an "Authorization: Bearer &lt;jwt&gt;" header names, or null when it names nobody
    /// readable — no header, not the bearer scheme, or an opaque token such as a Graph one.
    /// </summary>
    public static string? Caller(string? authorization)
    {
        var token = Bearer(authorization);
        if (token is null)
        {
            return null;
        }

        // Sliced rather than Split: this runs once per proxied request, and splitting would copy
        // all three segments to reach the one in the middle.
        var afterHeader = token.IndexOf('.');
        if (afterHeader < 0)
        {
            return null;
        }

        var rest = token.AsSpan(afterHeader + 1);
        var length = rest.IndexOf('.');

        // A length of -1 means there is no signature segment, so this is not a JWT.
        if (length is <= 0 or > MaxPayloadChars)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(rest[..length]));

            if (payload.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            foreach (var claim in CallerClaims)
            {
                if (payload.RootElement.TryGetProperty(claim, out var value) &&
                    value.ValueKind is JsonValueKind.String &&
                    value.GetString() is { Length: > 0 } caller)
                {
                    return caller;
                }
            }
        }
        catch (Exception)
        {
            // Anything unreadable is simply not a caller we can name.
            //
            // Deliberately unfiltered. This runs on the request path with no job but to label a
            // row, so no token — however malformed — may turn into a failed request. Naming the
            // types instead is what broke: JsonDocument defers UTF-8 validation to GetString, so
            // a claim holding invalid UTF-8 throws InvalidOperationException rather than the
            // JsonException the parse leads you to expect.
        }

        return null;
    }

    /// <summary>The token out of an "Authorization: Bearer &lt;token&gt;" header, or null.</summary>
    public static string? Bearer(string? header)
    {
        const string prefix = "Bearer ";

        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}
