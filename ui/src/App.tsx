import { useState } from 'react'
import { Badge, Button, Group, AppShell, Tabs, Text, Title } from '@mantine/core'
import { authConfig, signIn, signOut, useAuth } from './auth'
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

          <SignInButton onExplain={() => changeTab('tokens')} />
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
 * Always present, so signing in is never something you have to go looking for. When Entra ID has
 * not been configured it sends you to the Tokens tab, which says what is missing.
 */
function SignInButton({ onExplain }: { onExplain: () => void }) {
  const { account, error } = useAuth()
  const [busy, setBusy] = useState(false)

  async function run(action: () => Promise<void>) {
    setBusy(true)
    try {
      await action()
    } finally {
      setBusy(false)
    }
  }

  if (account) {
    return (
      <Group gap="xs" wrap="nowrap" mt={2}>
        <Badge variant="light" color="teal">
          {account.name || account.username}
        </Badge>
        <Button size="xs" variant="default" loading={busy} onClick={() => run(signOut)}>
          Log out
        </Button>
      </Group>
    )
  }

  return (
    <Group gap="xs" wrap="nowrap" mt={2}>
      {error && (
        <Text size="xs" c="red" maw={320} lineClamp={2} ta="right">
          {error}
        </Text>
      )}
      <Button
        size="xs"
        loading={busy}
        onClick={() => (authConfig().enabled ? run(signIn) : onExplain())}
      >
        Log in
      </Button>
    </Group>
  )
}
