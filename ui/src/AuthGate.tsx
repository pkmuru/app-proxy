import { useMemo } from 'react'
import { InteractionType } from '@azure/msal-browser'
import type { IPublicClientApplication } from '@azure/msal-browser'
import { MsalAuthenticationTemplate, MsalProvider } from '@azure/msal-react'
import type { MsalAuthenticationResult } from '@azure/msal-react'
import { Alert, Center, Code, Loader, Text } from '@mantine/core'
import App from './App'
import { authConfig } from './auth'

/**
 * Signs the user in before the app is usable: the template starts the redirect when there is no
 * account, renders the app once there is one, and shows what went wrong instead of the app when
 * Entra ID refuses. Nothing below it has to ask whether there is a user.
 */
export function AuthGate({ instance }: { instance: IPublicClientApplication }) {
  // The tab travels to Entra ID as state and is put back on the way in, since MSAL is told not to
  // navigate back to where sign-in started. Held still across renders: it is what the sign-in is
  // made from, not something to redo when the app repaints.
  const request = useMemo(() => ({ scopes: authConfig().scopes, state: window.location.hash }), [])

  return (
    <MsalProvider instance={instance}>
      <MsalAuthenticationTemplate
        interactionType={InteractionType.Redirect}
        authenticationRequest={request}
        loadingComponent={SigningIn}
        errorComponent={SignInFailed}
      >
        <App />
      </MsalAuthenticationTemplate>
    </MsalProvider>
  )
}

function SigningIn() {
  return (
    <Center h="100vh">
      <Loader />
    </Center>
  )
}

function SignInFailed({ error }: MsalAuthenticationResult) {
  return (
    <Center h="100vh" p="lg">
      <Alert color="red" title="Sign-in failed" maw={640}>
        <Text size="sm">{error?.message}</Text>
        <Text size="sm" mt="xs">
          The app registration needs a <b>Single-page application</b> platform whose redirect URI is{' '}
          <Code>{window.location.origin}</Code> — a Web platform will not do, MSAL.js refuses to
          redeem the code cross-origin.
        </Text>
      </Alert>
    </Center>
  )
}
