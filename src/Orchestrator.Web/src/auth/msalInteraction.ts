const interactionStatusKey = 'msal.interaction.status'

export function clearStaleMsalInteraction(
  storage: Pick<Storage, 'removeItem'> = sessionStorage,
) {
  storage.removeItem(interactionStatusKey)
}
