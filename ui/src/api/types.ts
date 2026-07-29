// Mirrors the C# records in src/StaatAppProxy. ASP.NET serialises them as camelCase.

export type ProxyMode = 'rest' | 'sse'
export type SseMode = 'array' | 'concat'
export type AuthMode = 'none' | 'passthrough' | 'obo'

export interface ProxyEndpoint {
  name: string
  routePrefix: string
  backendBaseUrl: string
  mode: ProxyMode
  sseMode: SseMode
  auth: AuthMode
  oboScopes: string[]
  timeoutSeconds: number
  enabled: boolean
}

export interface ProxyConfig {
  sourcePath: string
  oboConfigured: boolean
  endpoints: ProxyEndpoint[]
}

export interface TrafficSummary {
  id: string
  timestamp: string
  method: string
  path: string
  endpointName: string | null
  statusCode: number
  durationMs: number
  error: string | null
}

export interface CapturedExchange extends TrafficSummary {
  targetUrl: string | null
  requestHeaders: Record<string, string>
  requestBody: string | null
  responseHeaders: Record<string, string>
  responseBody: string | null
}
