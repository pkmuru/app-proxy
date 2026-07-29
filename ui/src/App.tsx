import { useState } from 'react'
import { Badge, Group, AppShell, Tabs, Text, Title } from '@mantine/core'
import { authConfig, useAccount } from './auth'
import { EndpointsPage } from './pages/EndpointsPage'
import { TrafficPage } from './pages/TrafficPage'
import { TestPage } from './pages/TestPage'

const TABS = ['endpoints', 'traffic', 'test'] as const
type Tab = (typeof TABS)[number]

// The hash keeps a reload (or a shared link) on the same tab without pulling in a router.
function tabFromHash(): Tab {
  const hash = window.location.hash.replace('#', '')
  return TABS.includes(hash as Tab) ? (hash as Tab) : 'endpoints'
}

export default function App() {
  const [tab, setTab] = useState<Tab>(tabFromHash)
  const account = useAccount()

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

          {authConfig().enabled && (
            <Badge variant="light" color={account ? 'teal' : 'gray'} mt={4}>
              {account ? account.name || account.username : 'not signed in'}
            </Badge>
          )}
        </Group>

        <Tabs value={tab} onChange={changeTab}>
          <Tabs.List>
            <Tabs.Tab value="endpoints">Endpoints</Tabs.Tab>
            <Tabs.Tab value="traffic">Traffic</Tabs.Tab>
            <Tabs.Tab value="test">Test</Tabs.Tab>
          </Tabs.List>
        </Tabs>
      </AppShell.Header>

      <AppShell.Main>
        {tab === 'endpoints' && <EndpointsPage />}
        {tab === 'traffic' && <TrafficPage />}
        {tab === 'test' && <TestPage />}
      </AppShell.Main>
    </AppShell>
  )
}
