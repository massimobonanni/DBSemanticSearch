param location string
@minLength(6)
param token string
param tags object
param backendResourceId string

resource web 'Microsoft.Web/staticSites@2025-03-01' = {
  name: 'swa-${token}'
  location: 'centralus'
  tags: union(tags, { 'azd-service-name': 'web' })
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {}
}

resource linkedApi 'Microsoft.Web/staticSites/linkedBackends@2025-03-01' = {
  parent: web
  name: uniqueString(backendResourceId)
  properties: {
    backendResourceId: backendResourceId
    region: location
  }
}

output webName string = web.name
output webUrl string = 'https://${web.properties.defaultHostname}'
