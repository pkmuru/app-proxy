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

// Held here rather than read back from MSAL on every render: useSyncExternalStore needs a stable
// reference, and MSAL rebuilds the account object each time it is asked for one.
let account: AccountInfo | null = null

const listeners = new Set<() => void>()

function setAccount(next: AccountInfo | null) {
  account = next
  listeners.forEach((listener) => listener())
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
    setAccount(existing)
  }

  return config
}

export function authConfig(): ClientAuthConfig {
  return config
}

export function useAccount(): AccountInfo | null {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => account,
  )
}

export async function signIn(): Promise<void> {
  if (!msal) return

  const result = await msal.loginPopup({ scopes: config.scopes })
  msal.setActiveAccount(result.account)
  setAccount(result.account)
}

export async function signOut(): Promise<void> {
  if (!msal) return

  await msal.logoutPopup({ account: msal.getActiveAccount() ?? undefined })
  setAccount(null)
}

/** Silent where possible, falling back to a popup when Entra ID wants to see the user again. */
export async function accessToken(): Promise<string | null> {
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
