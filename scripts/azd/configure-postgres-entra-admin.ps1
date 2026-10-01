$ErrorActionPreference = 'Stop'

$requiredVariables = @(
    'AZURE_SUBSCRIPTION_ID',
    'AZURE_RESOURCE_GROUP',
    'AZURE_POSTGRES_SERVER_NAME',
    'AZURE_POSTGRES_ENTRA_ADMIN_OBJECT_ID',
    'AZURE_POSTGRES_ENTRA_ADMIN_NAME',
    'AZURE_POSTGRES_ENTRA_ADMIN_PRINCIPAL_TYPE'
)

foreach ($name in $requiredVariables) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Required azd environment variable '$name' is not set."
    }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is required by the PostgreSQL Entra administrator hook.'
}

$showArguments = @(
    'postgres', 'flexible-server', 'show',
    '--subscription', $env:AZURE_SUBSCRIPTION_ID,
    '--resource-group', $env:AZURE_RESOURCE_GROUP,
    '--name', $env:AZURE_POSTGRES_SERVER_NAME,
    '--query', 'state',
    '--output', 'tsv',
    '--only-show-errors'
)

$serverReady = $false
for ($attempt = 1; $attempt -le 24; $attempt++) {
    $stateOutput = & az @showArguments
    $showExitCode = $LASTEXITCODE
    $state = ($stateOutput -join '').Trim()

    if ($showExitCode -eq 0 -and $state -eq 'Ready') {
        $serverReady = $true
        break
    }

    if ($state -eq 'Failed') {
        throw 'PostgreSQL Flexible Server entered the Failed state.'
    }

    if ($attempt -lt 24) {
        Write-Host "Waiting for PostgreSQL Flexible Server readiness (attempt $attempt/24; state: $state)."
        Start-Sleep -Seconds 15
    }
}

if (-not $serverReady) {
    throw 'PostgreSQL Flexible Server did not become Ready within six minutes.'
}

$templatePath = Join-Path $PSScriptRoot '../../infra/postgres-entra-admin.bicep'
$deploymentArguments = @(
    'deployment', 'group', 'create',
    '--name', 'postgres-entra-admin',
    '--subscription', $env:AZURE_SUBSCRIPTION_ID,
    '--resource-group', $env:AZURE_RESOURCE_GROUP,
    '--template-file', $templatePath,
    '--parameters',
    "serverName=$env:AZURE_POSTGRES_SERVER_NAME",
    "entraAdminObjectId=$env:AZURE_POSTGRES_ENTRA_ADMIN_OBJECT_ID",
    "entraAdminName=$env:AZURE_POSTGRES_ENTRA_ADMIN_NAME",
    "entraAdminPrincipalType=$env:AZURE_POSTGRES_ENTRA_ADMIN_PRINCIPAL_TYPE",
    '--only-show-errors',
    '--output', 'none'
)

for ($attempt = 1; $attempt -le 5; $attempt++) {
    & az @deploymentArguments
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Configured the PostgreSQL Microsoft Entra administrator.'
        exit 0
    }

    if ($attempt -lt 5) {
        Write-Host "Administrator configuration failed; retrying (attempt $attempt/5)."
        Start-Sleep -Seconds 15
    }
}

throw 'Unable to configure the PostgreSQL Microsoft Entra administrator after five attempts.'