targetScope = 'subscription'

param environmentName string

@metadata({ azd: { type: 'location' } })
param location string

@secure()
param postgresPassword string

var token = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
}

resource group 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}-${token}'
  location: location
  tags: tags
}

module logAnalytics 'log-analytics.bicep' = {
  name: 'semantic-search-log-analytics'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
  }
}

module appInsightsFrontend 'appinsights-frontend.bicep' = {
  name: 'semantic-search-appinsights-frontend'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
    workspaceId: logAnalytics.outputs.workspaceId
  }
}

module appInsightsBackend 'appinsights-backend.bicep' = {
  name: 'semantic-search-appinsights-backend'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
    workspaceId: logAnalytics.outputs.workspaceId
  }
}

module appInsightsFoundry 'appinsights-foundry.bicep' = {
  name: 'semantic-search-appinsights-foundry'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
    workspaceId: logAnalytics.outputs.workspaceId
  }
}

module database 'database.bicep' = {
  name: 'semantic-search-database'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
    postgresPassword: postgresPassword
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
    postgresPassword: postgresPassword
    databaseHost: database.outputs.host
    databaseName: database.outputs.name
    foundryName: foundry.outputs.name
    foundryEndpoint: foundry.outputs.endpoint
    embeddingDeploymentName: foundry.outputs.embeddingDeploymentName
    embeddingDimensions: foundry.outputs.embeddingDimensions
    applicationInsightsConnectionString: appInsightsBackend.outputs.connectionString
  }
}

module frontend 'frontend.bicep' = {
  name: 'semantic-search-frontend'
  scope: group
  params: {
    location: location
    token: token
    tags: tags
    backendResourceId: backend.outputs.functionId
  }
}

output AZURE_RESOURCE_GROUP string = group.name
output AZURE_FUNCTION_NAME string = backend.outputs.functionName
output AZURE_FOUNDRY_ENDPOINT string = foundry.outputs.endpoint
output AZURE_FOUNDRY_EMBEDDING_DEPLOYMENT string = foundry.outputs.embeddingDeploymentName
output AZURE_FOUNDRY_LARGE_EMBEDDING_DEPLOYMENT string = foundry.outputs.largeEmbeddingDeploymentName
output AZURE_STATIC_WEB_APP_NAME string = frontend.outputs.webName
output AZURE_STATIC_WEB_APP_URL string = frontend.outputs.webUrl
output AZURE_APPINSIGHTS_FRONTEND_CONNECTION_STRING string = appInsightsFrontend.outputs.connectionString
output AZURE_APPINSIGHTS_BACKEND_CONNECTION_STRING string = appInsightsBackend.outputs.connectionString
output AZURE_APPINSIGHTS_FOUNDRY_CONNECTION_STRING string = appInsightsFoundry.outputs.connectionString
