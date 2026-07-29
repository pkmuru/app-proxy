import type { CapturedExchange, ProxyConfig, TrafficSummary } from './types'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)

  if (!response.ok) {
    throw new Error(`${init?.method ?? 'GET'} ${path} failed: ${response.status} ${response.statusText}`)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export const api = {
  config: () => request<ProxyConfig>('/admin/api/config'),
  traffic: () => request<TrafficSummary[]>('/admin/api/traffic'),
  exchange: (id: string) => request<CapturedExchange>(`/admin/api/traffic/${id}`),
  clearTraffic: () => request<void>('/admin/api/traffic', { method: 'DELETE' }),
}
