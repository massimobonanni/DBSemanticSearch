param serverName string
param entraAdminObjectId string
param entraAdminName string
@allowed([
  'Group'
  'ServicePrincipal'
  'User'
])
param entraAdminPrincipalType string

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: serverName
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
