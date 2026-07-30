import { useSyncExternalStore } from 'react'
import type { AccountInfo, IPublicClientApplication } from '@azure/msal-browser'

export interface ClientAuthConfig {
  enabled: boolean
  clientId: string
  authority: string
  scopes: string[]
}

const DISABLED: ClientAuthConfig = { enabled: false, clientId: '', authority: '', scopes: [] }

let config: ClientAuthConfig = DISABLED
let msal: IPublicClientApplication | null = null

// MSAL is a large dependency and most deployments never configure sign-in, so it is loaded on
// demand rather than bundled into the initial download. Kept here for the instanceof check below.
let msalModule: typeof import('@azure/msal-browser') | null = null

export interface AuthState {
  account: AccountInfo | null
  /** The last sign-in failure, kept so the header button and the Tokens tab agree on what went wrong. */
  error: string | null
}

// Held here rather than read back from MSAL on every render: useSyncExternalStore needs a stable
// reference, and MSAL rebuilds the account object each time it is asked for one.
let state: AuthState = { account: null, error: null }

const listeners = new Set<() => void>()

function update(next: Partial<AuthState>) {
  state = { ...state, ...next }
  listeners.forEach((listener) => listener())
}

function describe(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

/**
 * Reads the Entra ID settings the API serves and, when they are present, brings MSAL up.
 *
 * Signing in is entirely optional — the proxy's own APIs are unauthenticated. It exists so a
 * developer can obtain a real user token and exercise endpoints configured with auth "obo".
 */
export async function initAuth(): Promise<ClientAuthConfig> {
  try {
    config = await fetch('/admin/api/client-auth').then((response) => response.json())
  } catch {
    // The UI is useful without sign-in, so a failure here must not stop it loading.
    return DISABLED
  }

  if (!config.enabled) return config

  try {
    msalModule = await import('@azure/msal-browser')
    msal = await msalModule.createStandardPublicClientApplication({
      auth: {
        clientId: config.clientId,
        authority: config.authority,
        redirectUri: window.location.origin,
      },
      // Session storage keeps the token out of other tabs and drops it when the browser closes.
      cache: { cacheLocation: 'sessionStorage' },
    })

    const [existing] = msal.getAllAccounts()
    if (existing) {
      msal.setActiveAccount(existing)
      update({ account: existing })
    }
  } catch (error) {
    // A tenant or client id that Entra ID will not recognise makes MSAL throw on the way up. That
    // must not take the rest of the admin UI down with it — least of all the page that would
    // explain the problem.
    update({ error: `Sign-in could not start: ${describe(error)}` })
  }

  return config
}

export function authConfig(): ClientAuthConfig {
  return config
}

export function useAuth(): AuthState {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => state,
  )
}

/**
 * Opens the Entra ID popup. Failures are recorded rather than thrown: the button that starts this
 * sits in the header with nowhere to show a message, so both it and the Tokens tab read the state.
 */
export async function signIn(): Promise<void> {
  if (!msal) {
    update({ error: state.error ?? 'Sign-in is unavailable: MSAL did not start.' })
    return
  }

  update({ error: null })

  try {
    const result = await msal.loginPopup({ scopes: config.scopes })
    msal.setActiveAccount(result.account)
    update({ account: result.account })
  } catch (error) {
    update({ error: describe(error) })
  }
}

export async function signOut(): Promise<void> {
  if (!msal) return

  try {
    await msal.logoutPopup({ account: msal.getActiveAccount() ?? undefined })
    update({ account: null, error: null })
  } catch (error) {
    update({ error: describe(error) })
  }
}

/** Silent where possible, falling back to a popup when Entra ID wants to see the user again. */
export async function accessToken(): Promise<string | null> {
  const account = state.account
  if (!msal || !account) return null

  try {
    const result = await msal.acquireTokenSilent({ scopes: config.scopes, account })
    return result.accessToken
  } catch (error) {
    if (msalModule && error instanceof msalModule.InteractionRequiredAuthError) {
      const result = await msal.acquireTokenPopup({ scopes: config.scopes, account })
      return result.accessToken
    }

    throw error
  }
}

/** Decodes the payload for display. The signature is not checked — this is a diagnostics view. */
export function decodeJwt(token: string): Record<string, unknown> | null {
  try {
    const payload = token.split('.')[1]
    return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as Record<string, unknown>
  } catch {
    return null
  }
}
