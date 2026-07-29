import { Code, Table, Text } from '@mantine/core'
import { prettyBody } from '../format'

/** Header name/value pairs, as captured. Nothing is masked. */
export function HeaderList({ headers }: { headers: Record<string, string> }) {
  const entries = Object.entries(headers)

  if (entries.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        No headers
      </Text>
    )
  }

  return (
    <Table withTableBorder verticalSpacing={4} horizontalSpacing="sm" layout="fixed">
      <Table.Tbody>
        {entries.map(([name, value]) => (
          <Table.Tr key={name}>
            <Table.Td w="32%">
              <Text size="xs" fw={600}>
                {name}
              </Text>
            </Table.Td>
            <Table.Td>
              <Text size="xs" style={{ wordBreak: 'break-all' }}>
                {value}
              </Text>
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  )
}

export function BodyBlock({ body }: { body: string | null }) {
  if (!body) {
    return (
      <Text size="sm" c="dimmed">
        Empty body
      </Text>
    )
  }

  return (
    <Code block style={{ maxHeight: 320, overflow: 'auto' }}>
      {prettyBody(body)}
    </Code>
  )
}
