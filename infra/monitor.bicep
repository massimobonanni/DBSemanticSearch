param location string
@minLength(6)
param token string
param tags object

var abbreviations = loadJsonContent('./abbreviations.json')
var workspaceName = '${abbreviations.operationalInsightsWorkspaces}${token}'
var insightsName = '${abbreviations.insightsComponents}-${token}'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: insightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

output connectionString string = insights.properties.ConnectionString
