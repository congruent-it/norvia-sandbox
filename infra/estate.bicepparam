using 'estate.bicep'

// azd sets these for the environment it is provisioning.
param environmentName = readEnvironmentVariable('AZURE_ENV_NAME')
param location = readEnvironmentVariable('AZURE_LOCATION')

// The hook sets this to the signed-in Azure CLI user.
param presenterPrincipalId = readEnvironmentVariable('ESTATE_PRESENTER_PRINCIPAL_ID', '')
