import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Alert,
  Badge,
  Button,
  Code,
  CopyButton,
  Divider,
  Group,
  Paper,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { api } from '../api/client'
import { accessToken, authConfig, decodeJwt, signIn, signOut, useAccount } from '../auth'
import { statusColor } from '../format'
import { BodyBlock, HeaderList } from '../components/Payload'

const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']

interface TestResult {
  status: number
  durationMs: number
  headers: Record<string, string>
  body: string
}

export function TestPage() {
  const auth = authConfig()
  const account = useAccount()
  const { data: config } = useQuery({ queryKey: ['config'], queryFn: api.config })

  const [method, setMethod] = useState('GET')
  const [path, setPath] = useState('/api/jp/todos/1')
  const [body, setBody] = useState('')
  const [attachToken, setAttachToken] = useState(true)

  const [result, setResult] = useState<TestResult | null>(null)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const sendsBody = method !== 'GET' && method !== 'DELETE'

  async function send() {
    setSending(true)
    setError(null)

    try {
      const headers: Record<string, string> = {}

      if (attachToken && account) {
        const token = await accessToken()
        if (token) headers.Authorization = `Bearer ${token}`
      }

      if (sendsBody && body.trim()) {
        headers['Content-Type'] = 'application/json'
      }

      const started = performance.now()
      const response = await fetch(path, {
        method,
        headers,
        body: sendsBody && body.trim() ? body : undefined,
      })
      const text = await response.text()

      const responseHeaders: Record<string, string> = {}
      response.headers.forEach((value, name) => {
        responseHeaders[name] = value
      })

      setResult({
        status: response.status,
        durationMs: Math.round(performance.now() - started),
        headers: responseHeaders,
        body: text,
      })
    } catch (sendError) {
      setError(sendError instanceof Error ? sendError.message : String(sendError))
    } finally {
      setSending(false)
    }
  }

  return (
    <Stack>
      <SignInCard />

      <Paper withBorder p="md">
        <Title order={5} mb="sm">
          Send a request
        </Title>

        <Stack gap="sm">
          <Group grow align="flex-end">
            <Select
              label="Configured endpoint"
              placeholder="Pick one to fill the path"
              data={(config?.endpoints ?? []).map((endpoint) => ({
                value: endpoint.routePrefix,
                label: `${endpoint.name} — ${endpoint.routePrefix}`,
              }))}
              onChange={(value) => value && setPath(value)}
              clearable
            />
            <Select label="Method" data={METHODS} value={method} onChange={(value) => value && setMethod(value)} />
          </Group>

          <TextInput
            label="Path"
            description="Relative to this service, so the request goes through the proxy"
            value={path}
            onChange={(event) => setPath(event.currentTarget.value)}
          />

          {sendsBody && (
            <Textarea
              label="Body"
              placeholder='{ "example": true }'
              autosize
              minRows={3}
              maxRows={10}
              value={body}
              onChange={(event) => setBody(event.currentTarget.value)}
            />
          )}

          <Group justify="space-between">
            <Switch
              size="sm"
              label="Attach my access token"
              checked={attachToken}
              disabled={!account}
              onChange={(event) => setAttachToken(event.currentTarget.checked)}
            />
            <Button onClick={send} loading={sending}>
              Send
            </Button>
          </Group>

          {attachToken && !account && auth.enabled && (
            <Text size="xs" c="dimmed">
              Sign in above to attach a bearer token. Without one, endpoints using <Code>obo</Code> answer 401.
            </Text>
          )}
        </Stack>
      </Paper>

      {error && (
        <Alert color="red" title="The request could not be sent">
          {error}
        </Alert>
      )}

      {result && (
        <Paper withBorder p="md">
          <Group mb="sm" gap="xs">
            <Title order={5}>Response</Title>
            <Badge variant="light" color={statusColor(result.status)}>
              {result.status}
            </Badge>
            <Text size="sm" c="dimmed">
              {result.durationMs} ms
            </Text>
          </Group>

          <Stack gap="sm">
            <HeaderList headers={result.headers} />
            <BodyBlock body={result.body} />
          </Stack>
        </Paper>
      )}
    </Stack>
  )
}

function SignInCard() {
  const auth = authConfig()
  const account = useAccount()

  const [token, setToken] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!auth.enabled) {
    return (
      <Alert color="gray" title="Sign-in is not configured">
        Set <Code>ClientAuth:TenantId</Code>, <Code>ClientAuth:ClientId</Code> and{' '}
        <Code>ClientAuth:Scopes</Code> to sign in here and get a real access token for testing{' '}
        <Code>obo</Code> endpoints. Requests below still work without one.
      </Alert>
    )
  }

  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)

    try {
      await action()
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : String(actionError))
    } finally {
      setBusy(false)
    }
  }

  const claims = token ? decodeJwt(token) : null

  return (
    <Paper withBorder p="md">
      <Group justify="space-between">
        <div>
          <Title order={5}>{account ? account.name || account.username : 'Not signed in'}</Title>
          <Text size="xs" c="dimmed">
            Scopes: {auth.scopes.join(', ') || '(none configured)'}
          </Text>
        </div>

        <Group gap="xs">
          {account && (
            <Button
              variant="default"
              size="xs"
              loading={busy}
              onClick={() => run(async () => setToken(await accessToken()))}
            >
              Show access token
            </Button>
          )}
          <Button
            size="xs"
            variant={account ? 'default' : 'filled'}
            loading={busy}
            onClick={() =>
              run(async () => {
                if (account) {
                  setToken(null)
                  await signOut()
                } else {
                  await signIn()
                }
              })
            }
          >
            {account ? 'Sign out' : 'Sign in'}
          </Button>
        </Group>
      </Group>

      {error && (
        <Alert color="red" title="Sign-in failed" mt="sm">
          {error}
        </Alert>
      )}

      {token && (
        <>
          <Divider my="sm" label="Access token" labelPosition="left" />

          <Group gap="xs" mb="xs">
            <CopyButton value={token}>
              {({ copied, copy }) => (
                <Button size="xs" variant="default" onClick={copy}>
                  {copied ? 'Copied' : 'Copy token'}
                </Button>
              )}
            </CopyButton>
            <Text size="xs" c="dimmed">
              Paste into curl or Postman as <Code>Authorization: Bearer …</Code>
            </Text>
          </Group>

          <Code block style={{ maxHeight: 120, overflow: 'auto', wordBreak: 'break-all' }}>
            {token}
          </Code>

          {claims && (
            <>
              <Text size="xs" c="dimmed" mt="sm" mb={4}>
                Claims — check <Code>aud</Code> matches this proxy's app registration, and{' '}
                <Code>scp</Code> covers what the backend expects.
              </Text>
              <Code block style={{ maxHeight: 240, overflow: 'auto' }}>
                {JSON.stringify(claims, null, 2)}
              </Code>
            </>
          )}
        </>
      )}
    </Paper>
  )
}
