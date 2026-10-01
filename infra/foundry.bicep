param location string
@minLength(6)
param token string
param tags object

@description('Capacity (thousands of tokens per minute) of the text-embedding-3-large deployment.')
param embeddingCapacity int = 10

var abbreviations = loadJsonContent('./abbreviations.json')
var foundryResourceName = '${abbreviations.aiFoundryAccounts}${token}'
var projectName = 'semantic-search'

resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: foundryResourceName
  location: location
  tags: tags
  kind: 'AIServices'
  identity: {
    type: 'SystemAssigned'
  }
  sku: { name: 'S0' }
  properties: {
    allowProjectManagement: true
    customSubDomainName: foundryResourceName
    publicNetworkAccess: 'Enabled'
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundry
  name: projectName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: 'DB Semantic Search'
    description: 'Foundry project for semantic search embedding deployments.'
  }
}

resource largeEmbeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: foundry
  name: 'text-embedding-3-large'
  sku: {
    name: 'GlobalStandard'
    capacity: embeddingCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }
  dependsOn: [project]
}

output id string = foundry.id
output name string = foundry.name
output endpoint string = foundry.properties.endpoint
output projectId string = project.id
output projectName string = project.name
output embeddingDeploymentName string = largeEmbeddingDeployment.name
output embeddingDimensions int = 3072

