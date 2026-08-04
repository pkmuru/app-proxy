import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { MantineProvider } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthGate } from './AuthGate'
import { createMsal, loadAuthConfig } from './auth'

import '@mantine/core/styles.css'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { refetchOnWindowFocus: false, retry: 1 },
  },
})

// Both settled before the first paint rather than flickering in afterwards: the Entra ID settings
// the server holds, and any sign-in coming back from it.
await loadAuthConfig()
const instance = await createMsal()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MantineProvider defaultColorScheme="auto">
      <QueryClientProvider client={queryClient}>
        <AuthGate instance={instance} />
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
)
