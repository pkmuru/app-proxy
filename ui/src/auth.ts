import { useCallback } from 'react'
import { InteractionRequiredAuthError, PublicClientApplication } from '@azure/msal-browser'
import type { IPublicClientApplication } from '@azure/msal-browser'
import { useAccount, useMsal } from '@azure/msal-react'

export interface ClientAuthConfig {
  clientId: string
  authority: string
  scopes: string[]
}

let config: ClientAuthConfig = { clientId: '', authority: '', scopes: [] }

/**
 * The Entra ID settings the API serves, read once before anything renders.
 *
 * Signing in is what makes the user token available, so endpoints configured with auth "obo" can
 * be exercised as a real person. The proxy's own APIs stay unauthenticated regardless.
 */
export async function loadAuthConfig(): Promise<ClientAuthConfig> {
  config = await fetch('/admin/api/client-auth').then((response) => response.json())
  return config
}

export function authConfig(): ClientAuthConfig {
  return config
}

/**
 * An access token for the signed-in user: silent where possible, handing the page over when Entra
 * ID wants to see them again. Resolves to null in that case, because the browser is already
 * leaving — the token is in the cache by the time the app comes back up.
 */
export function useAccessToken(): () => Promise<string | null> {
  const { instance } = useMsal()
  const account = useAccount()

  return useCallback(async () => {
    if (!account) return null

    const request = { scopes: config.scopes, account }

    try {
      return (await instance.acquireTokenSilent(request)).accessToken
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await instance.acquireTokenRedirect(request)
        return null
      }

      throw error
    }
  }, [instance, account])
}

/**
 * Brings MSAL up and finishes a sign-in coming back from Entra ID before React renders.
 *
 * <MsalProvider> calls handleRedirectPromise itself on mount, but with no say over how. This app
 * routes on the hash, and so does the authorization code flow's response, so the two share the
 * address bar: the code has to be redeemed where it lands rather than after navigating back to the
 * tab sign-in started from, because that navigation only changes the fragment, which does not
 * reload the page — leaving the code sitting in the URL unredeemed. Doing it here settles that
 * first; the provider's own call then finds a spent response and resolves to null.
 */
export async function createMsal(): Promise<IPublicClientApplication> {
  const msal = new PublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      redirectUri: window.location.origin,
    },
    // Session storage keeps the token out of other tabs and drops it when the browser closes.
    cache: { cacheLocation: 'sessionStorage' },
  })

  await msal.initialize()

  try {
    const redirected = await msal.handleRedirectPromise({ navigateToLoginRequestUrl: false })

    // useAccount() and the token calls read the active account, which MSAL does not set by itself.
    msal.setActiveAccount(redirected?.account ?? msal.getAllAccounts()[0] ?? null)

    if (redirected?.state) {
      // Back to the tab sign-in started from, which MSAL no longer navigates to itself. Replaced
      // rather than assigned, so the Back button does not lead to the bare URL.
      window.history.replaceState(null, '', redirected.state)
    }
  } catch {
    // Left to MsalAuthenticationTemplate, which tries the sign-in again and shows the reason if
    // that fails too. Throwing here would take the whole app down instead.
  }

  // MSAL takes the response out of the address bar as it redeems it. One still sitting there was
  // refused, and the hash router would read it as the name of a tab.
  if (/[#&](code|error)=/.test(window.location.hash)) {
    window.history.replaceState(null, '', window.location.pathname + window.location.search)
  }

  return msal
}

/** Decodes the payload for display. The signature is not checked — this is a diagnostics view. */
export function decodeJwt(token: string): Record<string, unknown> | null {
  try {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')

    // atob gives one byte per character, but claims are UTF-8 — read as Latin-1 a name with an
    // accent in it comes out mangled, and would disagree with the Caller column the server sends.
    const bytes = Uint8Array.from(atob(payload), (character) => character.charCodeAt(0))

    return JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>
  } catch {
    return null
  }
}
