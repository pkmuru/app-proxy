import { useMemo } from 'react'
import { Anchor, Code, Stack, Table, Text } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { formatJson } from '../format'
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

/**
 * A captured body, byte for byte as it went over the wire.
 *
 * Formatting is offered rather than applied. Re-serialising JSON rewrites whitespace and rewrites
 * escapes — a newline inside a string is `\n` on the wire whatever the source looked like — so a
 * pretty-printed view is a reconstruction, not the request. When the question is why a backend
 * rejected something, what it actually received is the answer, and that is what shows by default.
 */
export function BodyBlock({
  body,
  contentType,
}: {
  body: string | null
  /** What this body was declared as, so a body that contradicts it can be flagged. */
  contentType?: string
}) {
  const [formatted, format] = useDisclosure(false)

  // Bodies run to 64 KB and this re-renders on every toggle of any panel in the drawer.
  const asJson = useMemo(() => formatJson(body), [body])

  if (!body) {
    return (
      <Text size="sm" c="dimmed">
        Empty body
      </Text>
    )
  }

  // The caller's mistake, forwarded as bytes and answered by the backend with a 400 that rarely
  // explains itself. A cut body proves nothing — capture stops at 64 KB, mid-document.
  const malformed =
    asJson === null && /json/i.test(contentType ?? '') && !body.endsWith('…[truncated]')

  return (
    <Stack gap={4}>
      {malformed && (
        <Text size="xs" c="orange" fw={600}>
          Sent as {contentType}, but this is not valid JSON — the body below is what a backend
          would have had to parse.
        </Text>
      )}

      <Code block style={{ maxHeight: 320, overflow: 'auto' }}>
        {formatted && asJson ? asJson : body}
      </Code>

      {/* Nothing to offer when formatting would not change anything — an empty toggle next to a
          plain-text or already-indented body is just noise. */}
      {asJson !== null && asJson !== body && (
        <Anchor component="button" type="button" size="xs" onClick={format.toggle}>
          {formatted ? 'Show it as it was sent' : 'Format as JSON'}
        </Anchor>
      )}
    </Stack>
  )
}
