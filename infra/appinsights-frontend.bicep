param location string
@minLength(6)
param token string
param tags object
param workspaceId string

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-web-${token}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspaceId
  }
}

output connectionString string = insights.properties.ConnectionString
