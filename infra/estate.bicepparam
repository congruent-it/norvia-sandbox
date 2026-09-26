using 'estate.bicep'

// azd sets these for the environment it is provisioning.
param environmentName = readEnvironmentVariable('AZURE_ENV_NAME')
param location = readEnvironmentVariable('AZURE_LOCATION')

// The hook sets this to the signed-in Azure CLI user.
param presenterPrincipalId = readEnvironmentVariable('ESTATE_PRESENTER_PRINCIPAL_ID', '')

// The postdeploy hook stores the TMS API's address once Aspire has deployed it.
param tmsBaseUrl = readEnvironmentVariable('ESTATE_TMS_BASE_URL', '')

// The postdeploy hook stores the demo UIs' sign-in callbacks once Aspire has deployed them.
param demoUiRedirectUris = readEnvironmentVariable('ESTATE_DEMO_UI_REDIRECT_URIS', '')

// The postdeploy hook stores the Integration console's sign-in callback once Aspire has deployed it.
param integrationConsoleRedirectUris = readEnvironmentVariable('ESTATE_INTEGRATION_CONSOLE_REDIRECT_URIS', '')
