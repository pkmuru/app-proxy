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
| `sseMode`        | `array`   | `array`, `concat` or `typed` — see below. Only read when `mode` is `sse`     |
| `sseConcatField` | `value`   | Property taken from each event in `concat` mode. `""` joins whole payloads   |
| `auth`           | `none`    | `none`, `passthrough` or `obo`                                               |
| `oboScopes`      | `[]`      | Scopes for the On-Behalf-Of exchange. Required when `auth` is `obo`          |
| `forwardHeaders` | `[]`      | Caller headers this route lets through on top of the global allowlist        |
| `headers`        | `{}`      | Fixed headers added to every backend call, e.g. an API key                    |
| `timeoutSeconds` | `30`      | Budget for the whole call, including reading an SSE stream to the end        |
| `enabled`        | `true`    | Set `false` to take a route out of service without deleting it               |

A request to `/api/orders/123?q=x` is forwarded to `https://orders.internal/123?q=x`. When several
prefixes match, the longest one wins, so `/api/orders/archive` can go somewhere other than
`/api/orders`. Matching is on whole segments: `/api/orders` never matches `/api/ordersx`.

`/admin`, `/echo`, `/healthz` and `/dev` belong to the proxy and are rejected as route prefixes.

### SSE aggregation

All three modes read the stream to completion and return one JSON body with `Content-Type:
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
answer in pieces, token-by-token style. Given a stream like

```
data: {"value":"Hello","seq":1}
data: {"value":", streaming","seq":2}
data: {"value":" world!","seq":3}
data: {"finished":true}
```

it returns

```json
{ "result": "Hello, streaming world!" }
```

Only the `value` property of each event contributes — the text, not the envelope it arrived in.
Events without that property, such as the trailing `{"finished":true}`, add nothing. Change the
property with `sseConcatField`, or set it to `""` to join whole payloads as they arrive:

```json
{ "name": "chat", "mode": "sse", "sseMode": "concat", "sseConcatField": "delta" }
```

If no event carries the named property the raw payloads are joined instead, so a field name that
does not match shows you the stream rather than an empty result.

`"sseMode": "typed"` — for backends whose events say what they are. Use this when the stream carries
progress alongside the answer. Given

```
data: {"key":"answer","type":"start","value":null}
data: {"key":"answer","type":"status","value":"thinking"}
data: {"key":"answer","type":"streaming","value":"Hello"}
data: {"key":"answer","type":"streaming","value":" world"}
data: {"key":"suggest","type":"followup","value":["Tell me more","Why?"]}
data: {"key":"answer","type":"end","value":null}
```

it returns

```json
{ "result": "Hello world", "followup": ["Tell me more", "Why?"] }
```

Only `streaming` and `followup` contribute. `start`, `status` and `end` are dropped — under `concat`
they would land in the middle of the answer, which is the reason this mode exists. A `followup`
value is normally an array and its items are collected into one flat list. Some backends send that
array JSON-encoded into a string instead — `"value": "[\"Tell me more\",\"Why?\"]"` — which is
unpacked to the same flat list rather than kept as one long suggestion; a lone value counts as a
list of one. Both properties are always present, so no follow-ups gives `[]` rather than nothing at
all. `key` is not used, and neither is `sseConcatField` — the property names are fixed.

In `array` mode each event's `data` is parsed as JSON when it is valid JSON and kept as a string
otherwise, so a sentinel like `[DONE]` survives intact. In `concat` mode, if the joined result is
itself valid JSON it is returned directly rather than wrapped in `result` — which is what makes
streamed-JSON backends work. `typed` mode always returns the shape above.

If the backend answers an SSE endpoint with an error status, that response is relayed untouched
rather than aggregated.

### What backends actually receive

A backend should see an ordinary call from this service, not a relayed browser request — several
reject traffic that carries browser fingerprints. So headers are filtered by an **allowlist**, not
a denylist: anything not named is dropped, which means a header a client starts sending later
cannot leak by accident.

By default only these cross:

```json
"Forwarding": { "AllowedHeaders": [ "Accept", "Content-Type" ] }
```

plus `Authorization` according to the route's `auth` mode. Dropped are cookies, `Origin`,
`Referer`, the caller's `User-Agent`, `Sec-Fetch-*`, `sec-ch-ua-*`, `X-Forwarded-*`, `Via`,
`X-Requested-With` and everything else. In their place the proxy sends
`User-Agent: StaatAppProxy/<version>`.

`traceparent` is suppressed too. .NET adds it to outbound calls automatically, and it announces
that the request is one hop in a larger trace. Turning it off costs the trace link between this
service and a backend's own telemetry; spans within this service are unaffected.

Two escape hatches, per route:

```json
{
  "name": "orders",
  "forwardHeaders": ["X-Correlation-Id"],
  "headers": { "X-Api-Key": "…", "User-Agent": "LegacyClient/2.1" }
}
```

`forwardHeaders` lets specific caller headers through. `headers` adds fixed values the caller never
sees and wins over everything else, so it can also pin a `User-Agent` a fussy backend insists on.
Naming a header the proxy owns — `Authorization`, `Host`, `Content-Length` — in `forwardHeaders` is
a config error rather than a silent no-op, because the alternative is debugging a header that
vanished without explanation.

Values in `headers` appear in the Endpoints tab, which renders raw configuration. That is
consistent with the rest of this service showing tokens unmasked, but it does mean an API key there
is visible to anyone who can reach the UI.

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

### Signing in from the admin UI

Testing an `obo` endpoint needs a real user token, which normally means fishing one out of another
application. Instead the **Log in** button in the header signs you in and the **Tokens** tab uses
the result directly. It is optional and off until configured:

```json
"ClientAuth": {
  "TenantId": "<tenant guid>",
  "ClientId": "<SPA app registration client id>",
  "Scopes": ["api://<this proxy's client id>/access_as_user"]
}
```

This needs an app registration with a **Single-page application** platform (public client, no
secret) whose redirect URI is this service's origin — `http://localhost:5000` and
`http://localhost:5173` for local work. A **Web** platform will not do: MSAL.js redeems the
authorization code with a cross-origin `fetch`, which Entra ID only permits for SPA registrations,
and refuses with `AADSTS9002326` otherwise. A SPA platform can be added alongside an existing Web
one on the same registration. Give it delegated permission to the scope the proxy exposes, so the
token it receives has the proxy as its audience — exactly what the On-Behalf-Of exchange requires.

The settings are served from the API at `/admin/api/client-auth`, so Entra ID is configured in one
place rather than baked into the UI at build time. MSAL itself is only downloaded when sign-in is
configured, and if it fails to start the rest of the UI still loads and says so.

### The Tokens tab

Four steps, top to bottom, matching the order things go wrong in:

1. **Sign in** — or don't. Everything below works on a token pasted from curl, Postman or another
   application.
2. **The token** — decoded and checked. Expiry is shown as a badge, because an expired assertion is
   the most common cause of an exchange that "suddenly stopped working". `aud`, `scp`, `tid` and
   `appid` are surfaced first; the rest is one click away.
3. **Exchange it for a backend token** — pick a configured endpoint and it asks for exactly the
   scopes a real request through that route would, or name scopes directly to override. On success
   you get the backend token, decoded the same way. On failure you get Entra ID's error code, its
   correlation id, its raw response, and a plain-language note for the codes that come up often.
4. **App-only token** (collapsed) — a token for the proxy's own identity via client credentials. It
   cannot be used as an On-Behalf-Of assertion, but it does prove `AzureAd:ClientId` and
   `ClientSecret` are right, which narrows down an exchange failure considerably.

The same two exchanges are available without the UI:

```bash
curl -X POST localhost:5000/admin/api/tokens/obo \
  -H 'Content-Type: application/json' \
  -d '{"token":"<user token>","endpoint":"orders"}'      # or "scopes": ["api://.../.default"]

curl -X POST localhost:5000/admin/api/tokens/app \
  -H 'Content-Type: application/json' \
  -d '{"scopes":["api://.../.default"]}'
```

Both answer `200` with `"ok": false` when Entra ID refuses, rather than a 4xx. The refusal detail is
the entire point of the endpoint, and most HTTP clients discard the body of an error response.

---

## Diagnostics

**Echo** — `/echo/{anything}`, any verb. Replies with the method, path, query, every header, and
the body. Nothing is masked, including `Authorization`; seeing the real token is the point.

**Traffic** — the last 100 exchanges are held in memory and shown in the UI. Each one records all
four sides, in order:

1. **Client → proxy** — what arrived, headers and body untouched
2. **Proxy → backend** — what was actually sent on, after trimming and injection
3. **Backend → proxy** — what came back untransformed, including the raw SSE event stream
4. **Proxy → client** — what was returned, after any aggregation

The middle two are the point. Header trimming and SSE aggregation mean that what a client asked for
and what a backend saw are deliberately different, and without both halves a mismatch is very hard
to pin down.

An `Authorization: Bearer <jwt>` header is decoded where it appears, rather than shown as a blob:
who the token is for, the scopes or roles it carries, the tenant, the client that asked for it and
how long it has left. The list itself carries a **Caller** column, so who sent what is legible
without opening every row. On an `obo` route steps 1 and 2 hold *different* tokens — the caller's
assertion and the one minted for the backend — and reading them side by side is the fastest way to
confirm an exchange did what it should have.

Decoding is a display convenience and nothing more: the signature is never checked, the header is
captured and served exactly as it arrived, and the raw string is one click away under **Show raw
token**. Tokens that are deliberately opaque, such as Entra ID's for Microsoft Graph, simply say so.

When the proxy itself fails, the row carries the exception behind the message: its type, and the
full stack trace under **Show stack trace**, inner exceptions included — which is usually where the
cause actually is. The trace stays on the detail, never on the list, so polling it costs nothing.

A call that never gets an answer — connection refused, DNS failure, timeout — still records step 2,
with the reason in place of step 3. What was sent is precisely what needs examining then. Only a
request refused *before* anything went out, such as a missing bearer token or a failed token
exchange, shows no backend sections at all, and says so.

Bodies are shown exactly as they went over the wire, down to the whitespace — re-serialising JSON
would rewrite spacing and escapes, and when the question is why a backend rejected something, what
it actually received is the answer. **Format as JSON** indents a copy for reading. A body sent as
`application/json` that is not JSON says so beside itself, since the proxy forwards bodies as bytes
without parsing them and that mistake otherwise surfaces only as an unexplained 400 from the
backend.

Bodies are cut at 64 KB and binary payloads are summarised rather than mangled. Nothing is written
to disk, and a restart clears it.

The UI has four tabs: **Endpoints** (the configuration in force, and where it was loaded from),
**Traffic** (recent exchanges, auto-refreshing every 5 seconds; click a row for the detail),
**Test** (send a request through any route, with your bearer token attached) and **Tokens** (get
one, read it, exchange it — see above).

The same data is available directly:

| Endpoint                    | Purpose                             |
| --------------------------- | ----------------------------------- |
| `GET /healthz`              | Liveness                            |
| `GET /admin/api/config`     | Endpoint configuration in force     |
| `GET /admin/api/client-auth` | Entra ID settings the UI signs in with |
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
  Api/                             echo, admin, tokens, health, dev SSE samples
  Diagnostics/                     the in-memory capture buffer
ui/
  src/auth.ts                      optional MSAL sign-in, loaded only when configured
  src/pages/                       Endpoints, Traffic, Test and Tokens tabs
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
