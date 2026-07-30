import { Anchor, Badge, Button, Code, Collapse, CopyButton, Group, Stack, Table, Text } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { decodeJwt } from '../auth'

/**
 * Claims worth reading first when a token is being questioned, in the order they usually matter.
 * Anything else is a click away under "all claims".
 */
const HIGHLIGHTS: Array<[claim: string, meaning: string]> = [
  ['aud', 'who the token is for — an On-Behalf-Of assertion must name this proxy'],
  ['scp', 'delegated scopes it carries'],
  ['roles', 'app roles, on an app-only token'],
  ['tid', 'tenant'],
  ['appid', 'the client that asked for it'],
  ['azp', 'the client that asked for it (v2 tokens)'],
  ['preferred_username', 'the user'],
  ['upn', 'the user (v1 tokens)'],
  ['ver', 'token version'],
]

/** Rounded to the largest unit that still says something useful. */
function duration(seconds: number): string {
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)} min`
  return `${(seconds / 3600).toFixed(1)} h`
}

function lifetime(claims: Record<string, unknown>): { label: string; color: string } {
  const exp = typeof claims.exp === 'number' ? claims.exp : null
  if (exp === null) return { label: 'no exp claim', color: 'gray' }

  const remaining = exp - Date.now() / 1000
  return remaining <= 0
    ? { label: `expired ${duration(-remaining)} ago`, color: 'red' }
    : { label: `valid for ${duration(remaining)}`, color: 'teal' }
}

function claimValue(value: unknown): string {
  return Array.isArray(value) ? value.join(' ') : String(value)
}

/** Decodes and displays one token. The signature is never checked — this is a diagnostics view. */
export function TokenView({ token, label }: { token: string; label?: string }) {
  const [rawOpen, raw] = useDisclosure(false)
  const [claimsOpen, allClaims] = useDisclosure(false)

  const claims = decodeJwt(token)

  if (!claims) {
    return (
      <Stack gap="xs">
        <Text size="sm" c="orange">
          Not a readable JWT. Entra ID access tokens for Microsoft Graph are deliberately opaque; a
          token for your own API should decode.
        </Text>
        <Code block style={{ maxHeight: 120, overflow: 'auto', wordBreak: 'break-all' }}>
          {token}
        </Code>
      </Stack>
    )
  }

  const age = lifetime(claims)
  const present = HIGHLIGHTS.filter(([claim]) => claims[claim] !== undefined)

  return (
    <Stack gap="xs">
      <Group gap="xs">
        {label && (
          <Text size="sm" fw={600}>
            {label}
          </Text>
        )}
        <Badge variant="light" color={age.color}>
          {age.label}
        </Badge>
        <CopyButton value={token}>
          {({ copied, copy }) => (
            <Button size="compact-xs" variant="default" onClick={copy}>
              {copied ? 'Copied' : 'Copy'}
            </Button>
          )}
        </CopyButton>
      </Group>

      <Table withTableBorder verticalSpacing={4} horizontalSpacing="sm" layout="fixed">
        <Table.Tbody>
          {present.map(([claim, meaning]) => (
            <Table.Tr key={claim}>
              <Table.Td w="22%">
                <Text size="xs" fw={600}>
                  {claim}
                </Text>
                <Text size="10px" c="dimmed">
                  {meaning}
                </Text>
              </Table.Td>
              <Table.Td>
                <Text size="xs" style={{ wordBreak: 'break-all' }}>
                  {claimValue(claims[claim])}
                </Text>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>

      <Group gap="md">
        <Anchor component="button" type="button" size="xs" onClick={allClaims.toggle}>
          {claimsOpen ? 'Hide' : 'Show'} all claims
        </Anchor>
        <Anchor component="button" type="button" size="xs" onClick={raw.toggle}>
          {rawOpen ? 'Hide' : 'Show'} raw token
        </Anchor>
      </Group>

      <Collapse expanded={claimsOpen}>
        <Code block style={{ maxHeight: 280, overflow: 'auto' }}>
          {JSON.stringify(claims, null, 2)}
        </Code>
      </Collapse>

      <Collapse expanded={rawOpen}>
        <Code block style={{ maxHeight: 160, overflow: 'auto', wordBreak: 'break-all' }}>
          {token}
        </Code>
      </Collapse>
    </Stack>
  )
}
