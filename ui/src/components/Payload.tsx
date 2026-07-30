import { Code, Table, Text } from '@mantine/core'
import { prettyBody } from '../format'
import { TokenView } from './TokenView'

/** The token out of an "Authorization: Bearer <token>" header value, or null for anything else. */
function bearerToken(name: string, value: string): string | null {
  if (name.toLowerCase() !== 'authorization') return null

  const match = /^bearer\s+(\S+)$/i.exec(value.trim())
  return match?.[1] ?? null
}

/**
 * Header name/value pairs, as captured. Nothing is masked: a bearer token is decoded so it can be
 * read at a glance, and the raw string is still there behind "Show raw token".
 */
export function HeaderList({
  headers,
  tokenLabel,
}: {
  headers: Record<string, string>
  /** Says which token this is, on the two sides of an exchange that can carry different ones. */
  tokenLabel?: string
}) {
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
      {/* A fixed layout takes its column widths from the first row, and that row may be a
          full-width token panel — so the widths are stated here, where row order cannot reach. */}
      <colgroup>
        <col style={{ width: '32%' }} />
        <col />
      </colgroup>

      <Table.Tbody>
        {entries.map(([name, value]) => {
          const token = bearerToken(name, value)

          // Given the whole row, so the claims table is not squeezed into the value column.
          if (token) {
            return (
              <Table.Tr key={name}>
                <Table.Td colSpan={2}>
                  <Text size="xs" fw={600} mb={6}>
                    {name}
                  </Text>
                  <TokenView token={token} label={tokenLabel} />
                </Table.Td>
              </Table.Tr>
            )
          }

          return (
            <Table.Tr key={name}>
              <Table.Td>
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
          )
        })}
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
