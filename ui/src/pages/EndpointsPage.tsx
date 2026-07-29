import { useQuery } from '@tanstack/react-query'
import { Alert, Anchor, Badge, Code, Collapse, Loader, Paper, Stack, Table, Text } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { api } from '../api/client'
import { authColor } from '../format'

export function EndpointsPage() {
  const { data, isPending, error } = useQuery({ queryKey: ['config'], queryFn: api.config })
  const [rawOpen, raw] = useDisclosure(false)

  if (isPending) return <Loader />

  if (error) {
    return (
      <Alert color="red" title="Could not load the configuration">
        {error.message}
      </Alert>
    )
  }

  const oboUnusable = !data.oboConfigured && data.endpoints.some((e) => e.auth === 'obo' && e.enabled)

  return (
    <Stack>
      <Text size="sm" c="dimmed">
        Loaded from <Code>{data.sourcePath}</Code> — edits to that file are picked up without a restart.
      </Text>

      <Alert color="gray" title="Backend calls are trimmed">
        Of the caller&apos;s headers only <Code>{data.allowedHeaders.join(', ')}</Code> are passed on, plus{' '}
        <Code>Authorization</Code> according to each route&apos;s auth mode. Cookies, <Code>Origin</Code>,{' '}
        <Code>Referer</Code>, the caller&apos;s <Code>User-Agent</Code> and <Code>X-Forwarded-*</Code> are
        dropped, so a backend sees a call from this service rather than a relayed browser request. Add
        exceptions per route with <Code>forwardHeaders</Code>.
      </Alert>

      {oboUnusable && (
        <Alert color="yellow" title="On-Behalf-Of is not configured">
          Endpoints below use <Code>obo</Code>, but <Code>AzureAd:TenantId</Code>, <Code>ClientId</Code> and{' '}
          <Code>ClientSecret</Code> are not set, so those calls will fail with 502.
        </Alert>
      )}

      <Paper withBorder>
        <Table striped highlightOnHover verticalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Route prefix</Table.Th>
              <Table.Th>Backend</Table.Th>
              <Table.Th>Mode</Table.Th>
              <Table.Th>Auth</Table.Th>
              <Table.Th>Timeout</Table.Th>
              <Table.Th>Status</Table.Th>
            </Table.Tr>
          </Table.Thead>

          <Table.Tbody>
            {data.endpoints.map((endpoint) => (
              <Table.Tr key={endpoint.name} opacity={endpoint.enabled ? 1 : 0.55}>
                <Table.Td>
                  <Text size="sm" fw={500}>
                    {endpoint.name}
                  </Text>
                </Table.Td>

                <Table.Td>
                  <Code>{endpoint.routePrefix}</Code>
                </Table.Td>

                <Table.Td>
                  <Text size="sm" style={{ wordBreak: 'break-all' }}>
                    {endpoint.backendBaseUrl}
                  </Text>

                  {endpoint.forwardHeaders.length > 0 && (
                    <Text size="xs" c="dimmed" mt={4}>
                      also forwards {endpoint.forwardHeaders.join(', ')}
                    </Text>
                  )}

                  {Object.keys(endpoint.headers).length > 0 && (
                    <Text size="xs" c="dimmed" mt={2}>
                      adds {Object.keys(endpoint.headers).join(', ')}
                    </Text>
                  )}
                </Table.Td>

                <Table.Td>
                  <Badge variant="light" color={endpoint.mode === 'sse' ? 'grape' : 'blue'}>
                    {endpoint.mode === 'sse' ? `sse · ${endpoint.sseMode}` : 'rest'}
                  </Badge>
                </Table.Td>

                <Table.Td>
                  <Badge variant="light" color={authColor(endpoint.auth)}>
                    {endpoint.auth}
                  </Badge>
                  {endpoint.auth === 'obo' && endpoint.oboScopes.length > 0 && (
                    <Text size="xs" c="dimmed" mt={4}>
                      {endpoint.oboScopes.join(', ')}
                    </Text>
                  )}
                </Table.Td>

                <Table.Td>
                  <Text size="sm">{endpoint.timeoutSeconds}s</Text>
                </Table.Td>

                <Table.Td>
                  <Badge variant="dot" color={endpoint.enabled ? 'teal' : 'gray'}>
                    {endpoint.enabled ? 'enabled' : 'disabled'}
                  </Badge>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Paper>

      <div>
        <Anchor component="button" type="button" size="sm" onClick={raw.toggle}>
          {rawOpen ? 'Hide' : 'Show'} raw configuration
        </Anchor>
        <Collapse expanded={rawOpen}>
          <Code block mt="xs">
            {JSON.stringify(data.endpoints, null, 2)}
          </Code>
        </Collapse>
      </div>
    </Stack>
  )
}
