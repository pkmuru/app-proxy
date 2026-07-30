import type { CapturedExchange, ProxyConfig, TokenResult, TrafficSummary } from './types'

/** Scopes named outright, or borrowed from a configured endpoint by name. */
export interface TokenAsk {
  token?: string
  scopes?: string[]
  endpoint?: string
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)

  if (!response.ok) {
    throw new Error(`${init?.method ?? 'GET'} ${path} failed: ${response.status} ${response.statusText}`)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

function post<T>(path: string, body: unknown): Promise<T> {
  return request<T>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export const api = {
  config: () => request<ProxyConfig>('/admin/api/config'),
  traffic: () => request<TrafficSummary[]>('/admin/api/traffic'),
  exchange: (id: string) => request<CapturedExchange>(`/admin/api/traffic/${id}`),
  clearTraffic: () => request<void>('/admin/api/traffic', { method: 'DELETE' }),
  oboToken: (ask: TokenAsk) => post<TokenResult>('/admin/api/tokens/obo', ask),
  appToken: (ask: TokenAsk) => post<TokenResult>('/admin/api/tokens/app', ask),
}
