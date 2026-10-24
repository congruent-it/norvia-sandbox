// The custom role behind the presenter panel's fault that disables a Logic App (docs/operations/as-found.md). It reads a site and starts or stops it, and
// nothing else: no write to its configuration, no run history, no other resource type. The estate assigns it to the Partner portal's managed identity on
// the one Logic App a fault disables, never on the resource group.

@description('The resource group the role may be assigned in.')
param assignableScope string

output roleDefinitionId string = logicAppSwitch.id

resource logicAppSwitch 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid('norvia-logic-app-switch', assignableScope)
  properties: {
    roleName: 'Norvia Logic App Switch (${uniqueString(assignableScope)})'
    description: 'Read, start and stop a Logic App Standard site, so the presenter panel can disable it for a few minutes and the fault\'s expiry can enable it again. Nothing else.'
    type: 'CustomRole'
    permissions: [
      {
        actions: [
          'Microsoft.Web/sites/read'
          'Microsoft.Web/sites/start/action'
          'Microsoft.Web/sites/stop/action'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
    assignableScopes: [
      assignableScope
    ]
  }
}
