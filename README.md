# Staat App Proxy

A plain REST front door for backend services, including ones that only speak Server-Sent Events.

Client apps here cannot consume SSE. This service takes an ordinary REST request, forwards it to
the right backend, reads the event stream to completion, and hands back a single JSON response.
It also proxies ordinary REST backends, swaps the caller's Entra ID token for a backend token
where that is needed, and keeps recent traffic in memory so you can see what actually went over
the wire.

**The APIs are deliberately unauthenticated, and the diagnostics show tokens in full. Run this on
an internal network only.**

---

## Getting started

Requirements: .NET 10 SDK and Node 20+.

```bash
# Terminal 1 — the API on http://localhost:5000
dotnet run --project src/StaatAppProxy

# Terminal 2 — the admin UI with hot reload on http://localhost:5173
cd ui && npm install && npm run dev
```

Open <http://localhost:5173> during development. The Vite dev server proxies `/admin`, `/echo`
and `/healthz` through to the API, so there is nothing to configure.

To run everything from the API alone, build the UI into the app's `wwwroot` first:

```bash
cd ui && npm run build      # writes to ../src/StaatAppProxy/wwwroot
dotnet run --project src/StaatAppProxy
```

Then the whole thing lives on <http://localhost:5000>. `npm run build` is also a prerequisite for
`dotnet publish` — without it you get a working API and a message where the UI should be.

Try it:

```bash
curl localhost:5000/api/jp/todos/1     # proxied to jsonplaceholder.typicode.com
curl localhost:5000/api/sse-demo       # an SSE stream folded into a JSON array
curl -X POST localhost:5000/echo/hi -d 'body' -H 'Authorization: Bearer abc'
```

`requests.http` has the full set, ready to run from VS Code or Rider.

---

## Configuring endpoints

Routes live in `src/StaatAppProxy/endpoints.json`. Edit the file and the change takes effect
immediately — no restart. A file that fails validation is logged and ignored, and the last good
configuration stays in force. YAML works too: point `EndpointConfig:Path` at a `.yml` file.

```json
{
  "endpoints": [
    {
      "name": "orders",
      "routePrefix": "/api/orders",
      "backendBaseUrl": "https://orders.internal",
      "mode": "rest",
      "auth": "obo",
      "oboScopes": ["api://orders-api/.default"],
      "timeoutSeconds": 30
    }
  ]
}
```

| Field            | Default   | Meaning                                                                     |
| ---------------- | --------- | --------------------------------------------------------------------------- |
| `name`           | required  | Unique id, shown in the UI and against captured traffic                      |
| `routePrefix`    | required  | Incoming path prefix that selects this endpoint                              |
| `backendBaseUrl` | required  | Absolute `http`/`https` base URL of the backend                              |
| `mode`           | `rest`    | `rest` passes the response through, `sse` aggregates an event stream         |
| `sseMode`        | `array`   | `array` or `concat` — see below. Only read when `mode` is `sse`              |
| `auth`           | `none`    | `none`, `passthrough` or `obo`                                               |
| `oboScopes`      | `[]`      | Scopes for the On-Behalf-Of exchange. Required when `auth` is `obo`          |
| `timeoutSeconds` | `30`      | Budget for the whole call, including reading an SSE stream to the end        |
| `enabled`        | `true`    | Set `false` to take a route out of service without deleting it               |

A request to `/api/orders/123?q=x` is forwarded to `https://orders.internal/123?q=x`. When several
prefixes match, the longest one wins, so `/api/orders/archive` can go somewhere other than
`/api/orders`. Matching is on whole segments: `/api/orders` never matches `/api/ordersx`.

`/admin`, `/echo`, `/healthz` and `/dev` belong to the proxy and are rejected as route prefixes.

### SSE aggregation

Both modes read the stream to completion and return one JSON body with `Content-Type:
application/json`.

`"sseMode": "array"` — one entry per event. Use this when each event is a separate result:

```json
[
  { "event": "message", "data": { "seq": 1 } },
  { "event": "message", "data": { "seq": 2 } },
  { "event": "done", "data": { "finished": true } }
]
```

`"sseMode": "concat"` — payloads joined in order. Use this when the backend streams one logical
answer in pieces, token-by-token style:

```json
{ "result": "Hello, streaming world!" }
```

Each event's `data` is parsed as JSON when it is valid JSON, and kept as a string otherwise — so a
sentinel like `[DONE]` survives intact. In `concat` mode, if the joined payload is itself valid
JSON it is returned directly rather than wrapped in `result`.

If the backend answers an SSE endpoint with an error status, that response is relayed untouched
rather than aggregated.

### Authentication modes

The proxy never validates the caller's token — the APIs are open by design. `auth` only decides
what reaches the backend.

- **`none`** — the caller's `Authorization` header is stripped.
- **`passthrough`** — it is forwarded unchanged.
- **`obo`** — the caller's bearer token is exchanged, via the Entra ID On-Behalf-Of flow, for a
  token scoped to the backend. A caller with no bearer token gets a 401 explaining what is
  missing; a failed exchange gets a 502 carrying the MSAL error code.

To use `obo`, register a confidential client in Entra ID and set:

```bash
export AzureAd__TenantId=<tenant guid>
export AzureAd__ClientId=<app registration client id>
export AzureAd__ClientSecret=<client secret>
```

The app registration needs delegated permissions for each downstream API named in `oboScopes`, and
the caller's token must have been issued for *this* app's audience. MSAL caches exchanged tokens
in memory per user and scope. Leave these unset and the proxy runs fine — only `obo` endpoints
fail, and the UI says so.

---

## Diagnostics

**Echo** — `/echo/{anything}`, any verb. Replies with the method, path, query, every header, and
the body. Nothing is masked, including `Authorization`; seeing the real token is the point.

**Traffic** — the last 100 exchanges are held in memory and shown in the UI, with full request and
response headers and bodies. Bodies are cut at 64 KB and binary payloads are summarised rather
than mangled. Nothing is written to disk, and a restart clears it.

The UI has two tabs: **Endpoints** (the configuration in force, and where it was loaded from) and
**Traffic** (recent exchanges, auto-refreshing every 5 seconds; click a row for the detail).

The same data is available directly:

| Endpoint                    | Purpose                             |
| --------------------------- | ----------------------------------- |
| `GET /healthz`              | Liveness                            |
| `GET /admin/api/config`     | Endpoint configuration in force     |
| `GET /admin/api/traffic`    | Recent exchanges, newest first      |
| `GET /admin/api/traffic/{id}` | One exchange in full              |
| `DELETE /admin/api/traffic` | Empty the capture buffer            |

In Development, `/dev/sse-sample` and `/dev/sse-tokens` are stand-in SSE backends so both
aggregation modes can be exercised without a real streaming service.

---

## Application Insights

Set a connection string and OpenTelemetry starts exporting traces, metrics and logs:

```bash
export ApplicationInsights__ConnectionString="InstrumentationKey=...;IngestionEndpoint=..."
```

Leave it empty and the app runs with no Azure dependency at all, which is the default for local
development.

## CORS

Browser clients call this service directly, so it handles CORS itself. By default any origin is
allowed, matching the fact that the APIs are unauthenticated. To restrict it:

```json
"Cors": { "AllowedOrigins": ["https://app.internal", "https://admin.internal"] }
```

With a list configured, credentials are permitted too. CORS headers coming back from a backend are
discarded — the browser must see one policy, this proxy's, not two conflicting ones.

---

## How it fits together

```
src/StaatAppProxy/
  Program.cs                       composition root and middleware order
  Config/                          endpoint model, validation, file loading with hot reload
  Proxy/  ProxyMiddleware.cs       matches a route, records the exchange
          Forwarder.cs             builds and sends the backend call, applies the auth mode
          SseAggregator.cs         folds an event stream into JSON
          HopByHopHeaders.cs       which headers must not cross the proxy
  Auth/   OboTokenService.cs       the On-Behalf-Of exchange
  Api/                             echo, admin, health, dev SSE samples
  Diagnostics/                     the in-memory capture buffer
ui/                                Vite + React + Mantine admin UI
```

A request that matches a configured prefix is handled by `ProxyMiddleware` and never reaches
routing; everything else falls through to the admin API and the UI's static files.

Both directions are buffered in memory so they can be recorded. That is the right trade for JSON
APIs, and the reason this proxy is not meant to carry large file transfers.

### Moving configuration to a remote service

`IEndpointConfigProvider` is the only thing the proxy knows about configuration. When the source
moves to another service, add an implementation and change the one registration in `Program.cs` —
nothing else needs to change.

### Not included

No rate limiting, retries, circuit breakers, response caching, request-body size limits or
authentication on the admin UI. Each is easy to add later; none of it is needed for what this
service currently does.
