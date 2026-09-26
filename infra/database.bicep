param location string
@minLength(6)
param token string
param tags object
param entraAdminObjectId string
param entraAdminName string
@allowed([
  'Group'
  'ServicePrincipal'
  'User'
])
param entraAdminPrincipalType string

var postgresName = 'psql-${token}'
var databaseName = 'semantic_search'

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresName
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: tenant().tenantId
    }
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7 }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource entraAdministrator 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: postgres
  name: entraAdminObjectId
  properties: {
    principalName: entraAdminName
    principalType: entraAdminPrincipalType
    tenantId: tenant().tenantId
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource vectorExtension 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: {
    value: 'VECTOR'
    source: 'user-override'
  }
}

resource azureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output host string = postgres.properties.fullyQualifiedDomainName
output name string = databaseName
output serverName string = postgres.name
