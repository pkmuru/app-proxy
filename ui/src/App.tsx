import { useState } from 'react'
import { AppShell, Tabs, Text, Title } from '@mantine/core'
import { EndpointsPage } from './pages/EndpointsPage'
import { TrafficPage } from './pages/TrafficPage'

const TABS = ['endpoints', 'traffic'] as const
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
        <Title order={4}>Staat App Proxy</Title>
        <Text size="xs" c="dimmed" mb={6}>
          A plain REST front door for REST and SSE backends
        </Text>

        <Tabs value={tab} onChange={changeTab}>
          <Tabs.List>
            <Tabs.Tab value="endpoints">Endpoints</Tabs.Tab>
            <Tabs.Tab value="traffic">Traffic</Tabs.Tab>
          </Tabs.List>
        </Tabs>
      </AppShell.Header>

      <AppShell.Main>{tab === 'endpoints' ? <EndpointsPage /> : <TrafficPage />}</AppShell.Main>
    </AppShell>
  )
}
