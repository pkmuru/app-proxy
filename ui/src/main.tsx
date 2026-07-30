import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { MantineProvider } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import { initAuth, isAuthResponseWindow } from './auth'

import '@mantine/core/styles.css'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { refetchOnWindowFocus: false, retry: 1 },
  },
})

if (isAuthResponseWindow()) {
  // Entra ID has just redirected back here, in a window MSAL is watching from the outside. Booting
  // the app would only get in the way of that; the address bar is the payload. Say something, in
  // case a person is looking at it, and touch nothing else.
  document.getElementById('root')!.textContent = 'Completing sign-in…'
} else {
  // MSAL has to know whether it is configured before anything renders, so the sign-in state is
  // settled on the first paint rather than flickering in afterwards.
  await initAuth()

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <MantineProvider defaultColorScheme="auto">
        <QueryClientProvider client={queryClient}>
          <App />
        </QueryClientProvider>
      </MantineProvider>
    </StrictMode>,
  )
}
