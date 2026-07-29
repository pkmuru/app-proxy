import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  Badge,
  Button,
  Code,
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
import { api } from '../api/client'
import type { CapturedExchange } from '../api/types'
import { formatTime, methodColor, statusColor } from '../format'
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

function ExchangeDetail({ exchange }: { exchange: CapturedExchange }) {
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

      {exchange.targetUrl && (
        <Text size="xs" c="dimmed" style={{ wordBreak: 'break-all' }}>
          Forwarded to {exchange.targetUrl}
        </Text>
      )}

      {exchange.error && (
        <Alert color="red" title="Proxy error">
          {exchange.error}
        </Alert>
      )}

      <Divider label="Request" labelPosition="left" />
      <HeaderList headers={exchange.requestHeaders} />
      <BodyBlock body={exchange.requestBody} />

      <Divider label="Response" labelPosition="left" />
      <HeaderList headers={exchange.responseHeaders} />
      <BodyBlock body={exchange.responseBody} />
    </Stack>
  )
}

