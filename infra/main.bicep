targetScope = 'subscription'

@minLength(1)
param environmentName string

@metadata({ azd: { type: 'location' } })
@minLength(1)
param location string

var abbreviations = loadJsonContent('./abbreviations.json')
var token = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
}

resource group 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: '${abbreviations.resourcesResourceGroups}${environmentName}'
  location: location
  tags: tags
}

module monitor 'monitor.bicep' = {
  name: 'semantic-search-monitor'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
  }
}

module database 'database.bicep' = {
  name: 'semantic-search-database'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
  }
}

module foundry 'foundry.bicep' = {
  name: 'semantic-search-foundry'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
  }
}

module backend 'backend.bicep' = {
  name: 'semantic-search-backend'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
    databaseHost: database.outputs.host
    databaseName: database.outputs.name
    foundryName: foundry.outputs.name
    foundryEndpoint: foundry.outputs.endpoint
    embeddingDeploymentName: foundry.outputs.embeddingDeploymentName
    embeddingDimensions: foundry.outputs.embeddingDimensions
    applicationInsightsConnectionString: monitor.outputs.connectionString
  }
}

module frontend 'frontend.bicep' = {
  name: 'semantic-search-frontend'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
    functionAppName: backend.outputs.functionName
  }
}

output AZURE_RESOURCE_GROUP string = group.name
output AZURE_FUNCTION_NAME string = backend.outputs.functionName
output AZURE_FUNCTION_URL string = backend.outputs.functionUrl
output AZURE_FUNCTION_PRINCIPAL_ID string = backend.outputs.principalId
output AZURE_POSTGRES_HOST string = database.outputs.host
output AZURE_POSTGRES_SERVER_NAME string = database.outputs.serverName
output AZURE_FOUNDRY_ENDPOINT string = foundry.outputs.endpoint
output AZURE_FOUNDRY_PROJECT_ID string = foundry.outputs.projectId
output AZURE_FOUNDRY_PROJECT_NAME string = foundry.outputs.projectName
output AZURE_FOUNDRY_EMBEDDING_DEPLOYMENT string = foundry.outputs.embeddingDeploymentName
output AZURE_WEB_APP_NAME string = frontend.outputs.webName
output AZURE_WEB_APP_URL string = frontend.outputs.webUrl
output AZURE_APPINSIGHTS_CONNECTION_STRING string = monitor.outputs.connectionString
