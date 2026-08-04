import { useState } from 'react'
import { useAccount, useMsal } from '@azure/msal-react'
import { Badge, Button, Group, AppShell, Tabs, Text, Title } from '@mantine/core'
import { EndpointsPage } from './pages/EndpointsPage'
import { TrafficPage } from './pages/TrafficPage'
import { TestPage } from './pages/TestPage'
import { TokensPage } from './pages/TokensPage'

const TABS = ['endpoints', 'traffic', 'test', 'tokens'] as const
type Tab = (typeof TABS)[number]

// The hash keeps a reload (or a shared link) on the same tab without pulling in a router.
function tabFromHash(): Tab {
  const hash = window.location.hash.replace('#', '')
  return TABS.includes(hash as Tab) ? (hash as Tab) : 'endpoints'
}

export default function App() {
  const [tab, setTab] = useState<Tab>(tabFromHash)

  function changeTab(value: string | null) {
    if (!value) return
    setTab(value as Tab)
    window.location.hash = value
  }

  return (
    <AppShell header={{ height: 96 }} padding="lg">
      <AppShell.Header px="lg" pt="sm">
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <div>
            <Title order={4}>Staat App Proxy</Title>
            <Text size="xs" c="dimmed" mb={6}>
              A plain REST front door for REST and SSE backends
            </Text>
          </div>

          <SignedInAs />
        </Group>

        <Tabs value={tab} onChange={changeTab}>
          <Tabs.List>
            <Tabs.Tab value="endpoints">Endpoints</Tabs.Tab>
            <Tabs.Tab value="traffic">Traffic</Tabs.Tab>
            <Tabs.Tab value="test">Test</Tabs.Tab>
            <Tabs.Tab value="tokens">Tokens</Tabs.Tab>
          </Tabs.List>
        </Tabs>
      </AppShell.Header>

      <AppShell.Main>
        {tab === 'endpoints' && <EndpointsPage />}
        {tab === 'traffic' && <TrafficPage />}
        {tab === 'test' && <TestPage />}
        {tab === 'tokens' && <TokensPage />}
      </AppShell.Main>
    </AppShell>
  )
}

/**
 * Who is signed in, and the way out. There is no way in: the app does not render at all until
 * there is an account, so this is only ever shown to someone who has one.
 */
function SignedInAs() {
  const { instance } = useMsal()
  const account = useAccount()

  if (!account) return null

  return (
    <Group gap="xs" wrap="nowrap" mt={2}>
      <Badge variant="light" color="teal">
        {account.name || account.username}
      </Badge>
      <Button size="xs" variant="default" onClick={() => instance.logoutRedirect({ account })}>
        Log out
      </Button>
    </Group>
  )
}
