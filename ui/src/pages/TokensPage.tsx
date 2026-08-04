import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Alert,
  Anchor,
  Badge,
  Button,
  Code,
  Collapse,
  Group,
  Paper,
  Select,
  Stack,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { api } from '../api/client'
import type { TokenResult } from '../api/types'
import { useAccount, useMsal } from '@azure/msal-react'
import { authConfig, useAccessToken } from '../auth'
import { TokenView } from '../components/TokenView'

/**
 * Everything to do with tokens in one place: sign in, look at what you got, exchange it for a
 * backend token, and read Entra ID's answer when it says no.
 */
export function TokensPage() {
  const { data: config } = useQuery({ queryKey: ['config'], queryFn: api.config })

  const [token, setToken] = useState('')

  const oboEndpoints = (config?.endpoints ?? []).filter((endpoint) => endpoint.auth === 'obo')

  return (
    <Stack>
      <SignInCard onToken={setToken} />

      <Paper withBorder p="md">
        <Title order={5}>2 · The token</Title>
        <Text size="xs" c="dimmed" mb="sm">
          Filled in by signing in above, or paste one from curl, Postman or another application.
        </Text>

        <Textarea
          placeholder="eyJ0eXAiOiJKV1QiLCJhbGc…"
          autosize
          minRows={3}
          maxRows={6}
          value={token}
          onChange={(event) => setToken(event.currentTarget.value)}
          styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)', fontSize: 11 } }}
        />

        {token.trim() && (
          <Stack mt="sm">
            <TokenView token={token.trim()} />
          </Stack>
        )}
      </Paper>

      <ExchangeCard token={token} endpoints={oboEndpoints} oboConfigured={config?.oboConfigured ?? false} />

      <AppTokenCard endpoints={oboEndpoints} oboConfigured={config?.oboConfigured ?? false} />
    </Stack>
  )
}

function SignInCard({ onToken }: { onToken: (token: string) => void }) {
  const auth = authConfig()
  const { instance } = useMsal()
  const account = useAccount()
  const getToken = useAccessToken()

  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  async function fetchToken() {
    setBusy(true)
    setFailure(null)

    try {
      const token = await getToken()
      if (token) onToken(token)
    } catch (tokenError) {
      setFailure(tokenError instanceof Error ? tokenError.message : String(tokenError))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Paper withBorder p="md">
      <Group justify="space-between" align="flex-start">
        <div>
          <Title order={5}>1 · {account ? account.name || account.username : 'Sign in'}</Title>
          <Text size="xs" c="dimmed">
            Scopes: {auth.scopes.join(', ') || '(none configured — the token will be for Microsoft Graph)'}
          </Text>
        </div>

        {account && (
          <Group gap="xs">
            <Button variant="default" size="xs" loading={busy} onClick={fetchToken}>
              Get access token
            </Button>
            <Button size="xs" variant="default" onClick={() => instance.logoutRedirect({ account })}>
              Log out
            </Button>
          </Group>
        )}
      </Group>

      {failure && (
        <Alert color="red" title="Entra ID refused" mt="sm">
          {failure}
        </Alert>
      )}
    </Paper>
  )
}

function ExchangeCard({
  token,
  endpoints,
  oboConfigured,
}: {
  token: string
  endpoints: { name: string; oboScopes: string[] }[]
  oboConfigured: boolean
}) {
  const [endpoint, setEndpoint] = useState<string | null>(null)
  const [scopes, setScopes] = useState('')
  const [result, setResult] = useState<TokenResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  const selected = endpoints.find((candidate) => candidate.name === endpoint)
  const typedScopes = scopes.split(/[\s,]+/).filter(Boolean)
  const ready = token.trim().length > 0 && (endpoint !== null || typedScopes.length > 0)

  async function exchange() {
    setBusy(true)
    setFailure(null)

    try {
      // Scopes typed here win, matching the server. Otherwise the endpoint's own are used, so the
      // exchange asks for exactly what a real request through that route would.
      setResult(
        await api.oboToken({
          token,
          ...(typedScopes.length > 0 ? { scopes: typedScopes } : { endpoint: endpoint ?? undefined }),
        }),
      )
    } catch (exchangeError) {
      setFailure(exchangeError instanceof Error ? exchangeError.message : String(exchangeError))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Paper withBorder p="md">
      <Title order={5}>3 · Exchange it for a backend token</Title>
      <Text size="xs" c="dimmed" mb="sm">
        The same On-Behalf-Of call a request through an <Code>obo</Code> route makes, run on its own so a
        failure is legible.
      </Text>

      {!oboConfigured && (
        <Alert color="yellow" mb="sm">
          <Code>AzureAd:TenantId</Code>, <Code>ClientId</Code> and <Code>ClientSecret</Code> are not set, so
          this will fail until they are.
        </Alert>
      )}

      <Stack gap="sm">
        <Select
          label="Endpoint"
          placeholder={endpoints.length > 0 ? 'Pick a route to use its scopes' : 'No obo endpoints configured'}
          data={endpoints.map((candidate) => ({ value: candidate.name, label: candidate.name }))}
          value={endpoint}
          onChange={setEndpoint}
          disabled={endpoints.length === 0}
          clearable
        />

        {selected && (
          <Text size="xs" c="dimmed" mt={-8}>
            Asks for <Code>{selected.oboScopes.join(' ')}</Code>
          </Text>
        )}

        <TextInput
          label="Or scopes"
          description="Space separated. Overrides the endpoint above when filled in."
          placeholder="api://<backend client id>/.default"
          value={scopes}
          onChange={(event) => setScopes(event.currentTarget.value)}
        />

        <Group justify="flex-end">
          <Button onClick={exchange} loading={busy} disabled={!ready}>
            Get OBO token
          </Button>
        </Group>

        {!token.trim() && (
          <Text size="xs" c="dimmed">
            Needs a token in step 2 — the exchange is of that token, not of your browser session.
          </Text>
        )}
      </Stack>

      {failure && (
        <Alert color="red" title="The request could not be sent" mt="sm">
          {failure}
        </Alert>
      )}

      {result && (
        <Stack mt="md">
          <ResultPanel result={result} />
        </Stack>
      )}
    </Paper>
  )
}

/** A token for the proxy itself. Separate card because it proves a different thing. */
function AppTokenCard({
  endpoints,
  oboConfigured,
}: {
  endpoints: { name: string; oboScopes: string[] }[]
  oboConfigured: boolean
}) {
  const [scopes, setScopes] = useState('')
  const [result, setResult] = useState<TokenResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [open, panel] = useDisclosure(false)

  const typedScopes = scopes.split(/[\s,]+/).filter(Boolean)
  const suggestion = endpoints[0]?.oboScopes[0] ?? 'api://<backend client id>/.default'

  async function acquire() {
    setBusy(true)
    try {
      setResult(await api.appToken({ scopes: typedScopes }))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Paper withBorder p="md">
      <Anchor component="button" type="button" size="sm" onClick={panel.toggle}>
        {open ? 'Hide' : 'Show'} app-only token
      </Anchor>

      <Collapse expanded={open}>
        <Text size="xs" c="dimmed" mt="xs" mb="sm">
          A token for this proxy&apos;s own identity, with no user involved. It cannot be exchanged
          On-Behalf-Of, but it does prove <Code>AzureAd:ClientId</Code> and <Code>ClientSecret</Code> are
          right — worth knowing before blaming the exchange. Client credentials only accept{' '}
          <Code>/.default</Code> scopes.
        </Text>

        {!oboConfigured && (
          <Alert color="yellow" mb="sm">
            <Code>AzureAd</Code> is not configured, so this will fail until it is.
          </Alert>
        )}

        <Group align="flex-end" grow>
          <TextInput
            label="Scopes"
            placeholder={suggestion}
            value={scopes}
            onChange={(event) => setScopes(event.currentTarget.value)}
          />
        </Group>

        <Group justify="flex-end" mt="sm">
          <Button variant="default" onClick={acquire} loading={busy} disabled={typedScopes.length === 0}>
            Get app token
          </Button>
        </Group>

        {result && (
          <Stack mt="md">
            <ResultPanel result={result} />
          </Stack>
        )}
      </Collapse>
    </Paper>
  )
}

function ResultPanel({ result }: { result: TokenResult }) {
  const [rawOpen, raw] = useDisclosure(false)

  if (result.ok && result.accessToken) {
    return (
      <Stack gap="xs">
        <Group gap="xs">
          <Badge variant="light" color="teal">
            acquired
          </Badge>
          {result.tokenSource && (
            <Text size="xs" c="dimmed">
              {result.tokenSource === 'Cache' ? 'served from the MSAL cache' : 'fetched from Entra ID'}
            </Text>
          )}
          {result.scopes && result.scopes.length > 0 && (
            <Text size="xs" c="dimmed" style={{ wordBreak: 'break-all' }}>
              · {result.scopes.join(' ')}
            </Text>
          )}
        </Group>

        <TokenView token={result.accessToken} />
      </Stack>
    )
  }

  return (
    <Alert color="red" title={result.error ?? 'The token could not be acquired'}>
      <Stack gap="xs">
        {result.detail && <Text size="sm">{result.detail}</Text>}

        {result.hint && (
          <Text size="sm" fw={500}>
            {result.hint}
          </Text>
        )}

        {result.correlationId && (
          <Text size="xs" c="dimmed">
            Correlation id <Code>{result.correlationId}</Code> — what support will ask for.
          </Text>
        )}

        {result.response && (
          <>
            <Anchor component="button" type="button" size="xs" onClick={raw.toggle}>
              {rawOpen ? 'Hide' : 'Show'} Entra ID&apos;s raw response
            </Anchor>
            <Collapse expanded={rawOpen}>
              <Code block style={{ maxHeight: 240, overflow: 'auto' }}>
                {result.response}
              </Code>
            </Collapse>
          </>
        )}
      </Stack>
    </Alert>
  )
}
