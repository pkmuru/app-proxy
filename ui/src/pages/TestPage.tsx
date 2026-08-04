import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Alert,
  Badge,
  Button,
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
import { useAccessToken } from '../auth'
import { header, statusColor } from '../format'
import { BodyBlock, HeaderList } from '../components/Payload'

const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']

interface TestResult {
  status: number
  durationMs: number
  headers: Record<string, string>
  body: string
}

export function TestPage() {
  const getToken = useAccessToken()
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

      if (attachToken) {
        const token = await getToken()
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
              onChange={(event) => setAttachToken(event.currentTarget.checked)}
            />
            <Button onClick={send} loading={sending}>
              Send
            </Button>
          </Group>
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
            <BodyBlock body={result.body} contentType={header(result.headers, 'content-type')} />
          </Stack>
        </Paper>
      )}
    </Stack>
  )
}
