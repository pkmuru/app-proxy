// Mirrors the C# records in src/StaatAppProxy. ASP.NET serialises them as camelCase.

export type ProxyMode = 'rest' | 'sse'
export type SseMode = 'array' | 'concat' | 'typed'
export type AuthMode = 'none' | 'passthrough' | 'obo'

export interface ProxyEndpoint {
  name: string
  routePrefix: string
  backendBaseUrl: string
  mode: ProxyMode
  sseMode: SseMode
  sseConcatField: string
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

/** Result of a token request. Failures come back as 200 with ok: false — the detail is the point. */
export interface TokenResult {
  ok: boolean

  accessToken: string | null
  expiresOn: string | null
  scopes: string[] | null
  tokenSource: string | null

  error: string | null
  detail: string | null
  correlationId: string | null
  response: string | null
  hint: string | null
}

export interface TrafficSummary {
  id: string
  timestamp: string
  method: string
  path: string
  endpointName: string | null

  /** Who the caller's bearer token names, or null when it names nobody readable. */
  caller: string | null

  statusCode: number
  durationMs: number
  error: string | null
}

export interface CapturedExchange extends TrafficSummary {
  targetUrl: string | null

  /** The exception behind `error` as text, inner exceptions and stack traces included. */
  exception: string | null

  // Client -> proxy
  requestHeaders: Record<string, string>
  requestBody: string | null

  // Proxy -> backend, after headers are trimmed to the allowlist. Null if it never got that far.
  backendRequestHeaders: Record<string, string> | null
  backendRequestBody: string | null

  // Backend -> proxy, untransformed: the raw event stream on an SSE route. All null with a
  // backendError set means the call went out and nothing came back.
  backendStatusCode: number | null
  backendResponseHeaders: Record<string, string> | null
  backendResponseBody: string | null
  backendError: string | null

  // Proxy -> client, after any SSE aggregation
  responseHeaders: Record<string, string>
  responseBody: string | null
}
