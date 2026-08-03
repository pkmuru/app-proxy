import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  Anchor,
  Badge,
  Button,
  Code,
  Collapse,
  Divider,
  Drawer,
  Group,
  Loader,
  Paper,
  Stack,
  Switch,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { api } from '../api/client'
import type { CapturedExchange } from '../api/types'
import { formatTime, header, methodColor, statusColor } from '../format'
import { BodyBlock, HeaderList } from '../components/Payload'

export function TrafficPage() {
  const queryClient = useQueryClient()
  const [live, setLive] = useState(true)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const { data, isPending, error } = useQuery({
    queryKey: ['traffic'],
    queryFn: api.traffic,
    refetchInterval: live ? 5000 : false,
  })

  const detail = useQuery({
    queryKey: ['traffic', selectedId],
    queryFn: () => api.exchange(selectedId!),
    enabled: selectedId !== null,
  })

  const clear = useMutation({
    mutationFn: api.clearTraffic,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['traffic'] }),
  })

  if (isPending) return <Loader />

  if (error) {
    return (
      <Alert color="red" title="Could not load captured traffic">
        {error.message}
      </Alert>
    )
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          The {data.length} most recent request{data.length === 1 ? '' : 's'}, newest first. Held in memory only —
          a restart clears them.
        </Text>

        <Group>
          <Switch
            size="sm"
            label="Auto refresh"
            checked={live}
            onChange={(event) => setLive(event.currentTarget.checked)}
          />
          <Button size="xs" variant="default" onClick={() => clear.mutate()} loading={clear.isPending}>
            Clear
          </Button>
        </Group>
      </Group>

      {data.length === 0 ? (
        <Alert color="gray" title="Nothing captured yet">
          Send a request through a configured route, or call <Code>/echo</Code>, and it will appear here.
        </Alert>
      ) : (
        <Paper withBorder>
          <Table striped highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={120}>Time</Table.Th>
                <Table.Th w={90}>Method</Table.Th>
                <Table.Th>Path</Table.Th>
                <Table.Th w={160}>Endpoint</Table.Th>
                <Table.Th w={170}>Caller</Table.Th>
                <Table.Th w={90}>Status</Table.Th>
                <Table.Th w={90}>Duration</Table.Th>
              </Table.Tr>
            </Table.Thead>

            <Table.Tbody>
              {data.map((row) => (
                <Table.Tr
                  key={row.id}
                  onClick={() => setSelectedId(row.id)}
                  style={{ cursor: 'pointer' }}
                >
                  <Table.Td>
                    <Text size="xs" ff="monospace">
                      {formatTime(row.timestamp)}
                    </Text>
                  </Table.Td>

                  <Table.Td>
                    <Badge variant="light" color={methodColor(row.method)}>
                      {row.method}
                    </Badge>
                  </Table.Td>

                  <Table.Td>
                    <Text size="sm" style={{ wordBreak: 'break-all' }}>
                      {row.path}
                    </Text>
                  </Table.Td>

                  <Table.Td>
                    <Text size="sm" c={row.endpointName ? undefined : 'dimmed'}>
                      {row.endpointName ?? '—'}
                    </Text>
                  </Table.Td>

                  <Table.Td>
                    <Text
                      size="sm"
                      truncate
                      title={row.caller ?? undefined}
                      c={row.caller ? undefined : 'dimmed'}
                    >
                      {row.caller ?? '—'}
                    </Text>
                  </Table.Td>

                  <Table.Td>
                    <Badge variant="light" color={statusColor(row.statusCode)}>
                      {row.statusCode}
                    </Badge>
                  </Table.Td>

                  <Table.Td>
                    <Text size="sm">{row.durationMs} ms</Text>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      )}

      <Drawer
        opened={selectedId !== null}
        onClose={() => setSelectedId(null)}
        position="right"
        size="xl"
        title={<Title order={5}>Exchange detail</Title>}
      >
        {detail.isPending && <Loader />}
        {detail.error && (
          <Alert color="red" title="Could not load this exchange">
            {detail.error.message}
          </Alert>
        )}
        {detail.data && <ExchangeDetail exchange={detail.data} />}
      </Drawer>
    </Stack>
  )
}

/**
 * Why steps 2 and 3 are missing. Worth distinguishing: the echo endpoint has no backend by design,
 * whereas a proxied route with no backend call means the proxy turned the request away itself.
 */
function NoBackend({ exchange }: { exchange: CapturedExchange }) {
  if (exchange.endpointName === 'echo') {
    return (
      <Alert color="gray" title="No backend to reach">
        <Code>/echo</Code> is the proxy's own endpoint — it replies with whatever it was given, so
        there is no forwarded request to compare against. Everything it saw is in step 1.
      </Alert>
    )
  }

  return (
    <Alert color="gray" title="Never reached a backend">
      {exchange.error
        ? 'The proxy turned this away itself, before any call went out. The reason is above.'
        : 'The proxy answered this itself, before any call went out — a missing bearer token, or a failed token exchange.'}
      {exchange.targetUrl && ` Target would have been ${exchange.targetUrl}.`}
    </Alert>
  )
}

/**
 * What went wrong, and the exception behind it. The trace is collapsed because the message is
 * usually enough — and open one click away for when it is not, which saves going back to the
 * server console to find out what a "Proxy error" actually was.
 */
function ProxyFailure({ error, exception }: { error: string | null; exception: string | null }) {
  const [traceOpen, trace] = useDisclosure(false)

  if (!error && !exception) return null

  return (
    <Alert color="red" title="Proxy error">
      <Stack gap={6}>
        {error && <Text size="sm">{error}</Text>}

        {exception && (
          <>
            <Anchor component="button" type="button" size="xs" onClick={trace.toggle}>
              {traceOpen ? 'Hide stack trace' : 'Show stack trace'}
            </Anchor>

            <Collapse expanded={traceOpen}>
              {/* Wrapped rather than scrolled sideways, unlike the body blocks below. A frame is
                  one long line with no structure to preserve, and left unwrapped it widens its
                  flex ancestors past the drawer — putting the end of every line out of reach. */}
              <Code
                block
                style={{
                  maxHeight: 320,
                  overflowY: 'auto',
                  fontSize: 11,
                  whiteSpace: 'pre-wrap',
                  overflowWrap: 'anywhere',
                }}
              >
                {exception}
              </Code>
            </Collapse>
          </>
        )}
      </Stack>
    </Alert>
  )
}

/**
 * All four sides of an exchange, in the order they happened. The two backend-facing halves are
 * the point: request headers are trimmed to an allowlist and an SSE stream is folded into JSON,
 * so what the client asked for and what the backend saw are deliberately not the same thing.
 */
function ExchangeDetail({ exchange }: { exchange: CapturedExchange }) {
  const reachedBackend = exchange.backendRequestHeaders !== null

  return (
    <Stack gap="sm">
      <Group gap="xs">
        <Badge variant="light" color={methodColor(exchange.method)}>
          {exchange.method}
        </Badge>
        <Badge variant="light" color={statusColor(exchange.statusCode)}>
          {exchange.statusCode}
        </Badge>
        <Text size="sm">{exchange.durationMs} ms</Text>
      </Group>

      <Code block>{exchange.path}</Code>

      <ProxyFailure error={exchange.error} exception={exchange.exception} />

      <Divider label="1 · Client → proxy" labelPosition="left" />
      <HeaderList headers={exchange.requestHeaders} tokenLabel="From the caller" />
      <BodyBlock body={exchange.requestBody} contentType={header(exchange.requestHeaders, 'content-type')} />

      {reachedBackend ? (
        <>
          <Divider label="2 · Proxy → backend" labelPosition="left" />
          {exchange.targetUrl && (
            <Code block style={{ wordBreak: 'break-all' }}>
              {exchange.method} {exchange.targetUrl}
            </Code>
          )}
          <HeaderList headers={exchange.backendRequestHeaders ?? {}} tokenLabel="Sent to the backend" />
          <BodyBlock
            body={exchange.backendRequestBody}
            contentType={header(exchange.backendRequestHeaders ?? {}, 'content-type')}
          />

          <Divider label="3 · Backend → proxy" labelPosition="left" />
          {exchange.backendError !== null ? (
            <Alert color="orange" title="The backend never answered">
              {exchange.backendError} The request above did go out, so what it contained is what to
              check — along with whether the target URL is reachable from this host at all.
            </Alert>
          ) : (
            <>
              {exchange.backendStatusCode !== null && (
                <Group gap="xs">
                  <Badge variant="light" color={statusColor(exchange.backendStatusCode)}>
                    {exchange.backendStatusCode}
                  </Badge>
                  <Text size="xs" c="dimmed">
                    as the backend answered, before any transformation
                  </Text>
                </Group>
              )}
              <HeaderList headers={exchange.backendResponseHeaders ?? {}} />
              <BodyBlock
                body={exchange.backendResponseBody}
                contentType={header(exchange.backendResponseHeaders ?? {}, 'content-type')}
              />
            </>
          )}
        </>
      ) : (
        <NoBackend exchange={exchange} />
      )}

      <Divider label="4 · Proxy → client" labelPosition="left" />
      <HeaderList headers={exchange.responseHeaders} />
      <BodyBlock body={exchange.responseBody} contentType={header(exchange.responseHeaders, 'content-type')} />
    </Stack>
  )
}

