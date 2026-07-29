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
  forwardHeaders: string[]
  headers: Record<string, string>
  timeoutSeconds: number
  enabled: boolean
}

export interface ProxyConfig {
  sourcePath: string
  oboConfigured: boolean
  allowedHeaders: string[]
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

  // Client -> proxy
  requestHeaders: Record<string, string>
  requestBody: string | null

  // Proxy -> backend, after headers are trimmed to the allowlist. Null if it never got that far.
  backendRequestHeaders: Record<string, string> | null
  backendRequestBody: string | null

  // Backend -> proxy, untransformed: the raw event stream on an SSE route.
  backendStatusCode: number | null
  backendResponseHeaders: Record<string, string> | null
  backendResponseBody: string | null

  // Proxy -> client, after any SSE aggregation
  responseHeaders: Record<string, string>
  responseBody: string | null
}
