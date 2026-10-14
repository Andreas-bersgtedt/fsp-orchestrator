import type { AccountInfo, EndSessionRequest } from '@azure/msal-browser'

type LogoutClient = {
  logoutRedirect: (request?: EndSessionRequest) => Promise<void>
}

export async function clearSessionAndLogout(
  client: LogoutClient,
  account: AccountInfo | undefined,
  postLogoutRedirectUri: string,
  clearSession: () => void,
) {
  clearSession()
  await client.logoutRedirect({
    account,
    postLogoutRedirectUri,
  })
}
