param location string
@minLength(6)
param token string
param tags object
param functionAppName string
param webAppName string

var abbreviations = loadJsonContent('./abbreviations.json')
var planName = '${abbreviations.webServerFarms}web-${token}'

resource functionApp 'Microsoft.Web/sites@2024-04-01' existing = {
  name: functionAppName
}

var functionHostKey = listKeys('${functionApp.id}/host/default', '2022-03-01').functionKeys.default

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'app'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: false
  }
}

resource web 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'web' })
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      webSocketsEnabled: true
    }
  }
}

resource appSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: web
  name: 'appsettings'
  properties: {
    WEBSITE_RUN_FROM_PACKAGE: '1'
    ApiBaseUrl: 'https://${functionApp.properties.defaultHostName}/'
    FunctionKey: functionHostKey
  }
}

output webName string = web.name
output webUrl string = 'https://${web.properties.defaultHostName}'
