param location string
@minLength(6)
param token string
param tags object

@description('Capacity (thousands of tokens per minute) of the text-embedding-3-small deployment.')
param smallEmbeddingCapacity int = 10

@description('Capacity (thousands of tokens per minute) of the text-embedding-3-large deployment.')
param largeEmbeddingCapacity int = 10

resource foundry 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'aoai-${token}'
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: 'aoai-${token}'
    publicNetworkAccess: 'Enabled'
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: foundry
  name: 'text-embedding-3-large'
  sku: {
    name: 'GlobalStandard'
    capacity: largeEmbeddingCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }
  dependsOn: [embeddingDeployment]
}

output id string = foundry.id
output name string = foundry.name
output endpoint string = foundry.properties.endpoint
output embeddingDeploymentName string = embeddingDeployment.name
output embeddingDimensions int = 3072

