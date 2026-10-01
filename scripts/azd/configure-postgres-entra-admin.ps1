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

$adminConfigured = $false
for ($attempt = 1; $attempt -le 5; $attempt++) {
    & az @deploymentArguments
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Configured the PostgreSQL Microsoft Entra administrator.'
        $adminConfigured = $true
        break
    }

    if ($attempt -lt 5) {
        Write-Host "Administrator configuration failed; retrying (attempt $attempt/5)."
        Start-Sleep -Seconds 15
    }
}

if (-not $adminConfigured) {
    throw 'Unable to configure the PostgreSQL Microsoft Entra administrator after five attempts.'
}

foreach ($name in @('AZURE_POSTGRES_HOST', 'AZURE_FUNCTION_NAME', 'AZURE_FUNCTION_PRINCIPAL_ID')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Required azd environment variable '$name' is not set."
    }
}

if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    throw 'psql is required to register the Function identity in PostgreSQL (e.g. winget install PostgreSQL.PostgreSQL, then add its bin folder to PATH).'
}

# Values are interpolated into SQL, so accept only the formats Azure generates.
$functionName = $env:AZURE_FUNCTION_NAME
$functionPrincipalId = $env:AZURE_FUNCTION_PRINCIPAL_ID
$parsedPrincipalId = [guid]::Empty
if ($functionName -notmatch '^[a-z0-9-]+$' -or -not [guid]::TryParse($functionPrincipalId, [ref]$parsedPrincipalId)) {
    throw 'AZURE_FUNCTION_NAME or AZURE_FUNCTION_PRINCIPAL_ID has an unexpected format.'
}

$databaseName = 'semantic_search'
$firewallRuleName = 'azd-postprovision-client'
$firewallArguments = @(
    '--subscription', $env:AZURE_SUBSCRIPTION_ID,
    '--resource-group', $env:AZURE_RESOURCE_GROUP,
    '--name', $env:AZURE_POSTGRES_SERVER_NAME,
    '--rule-name', $firewallRuleName,
    '--only-show-errors',
    '--output', 'none'
)

$clientIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').ToString().Trim()
if (-not [System.Net.IPAddress]::TryParse($clientIp, [ref]$null)) {
    throw 'Unable to determine the public IP address of this machine.'
}

& az postgres flexible-server firewall-rule create @firewallArguments --start-ip-address $clientIp --end-ip-address $clientIp
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to create the temporary PostgreSQL firewall rule.'
}

try {
    $createRoleSql = "SELECT pg_catalog.pgaadauth_create_principal_with_oid('$functionName', '$functionPrincipalId', 'service', false, false) WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '$functionName');"
    $grantSql = "CREATE EXTENSION IF NOT EXISTS vector; GRANT USAGE, CREATE ON SCHEMA public TO `"$functionName`";"

    $registered = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $env:PGPASSWORD = az account get-access-token --resource-type oss-rdbms --query accessToken --output tsv
        & psql "host=$env:AZURE_POSTGRES_HOST dbname=postgres user=$env:AZURE_POSTGRES_ENTRA_ADMIN_NAME sslmode=require" -v ON_ERROR_STOP=1 -q -c $createRoleSql
        if ($LASTEXITCODE -eq 0) {
            & psql "host=$env:AZURE_POSTGRES_HOST dbname=$databaseName user=$env:AZURE_POSTGRES_ENTRA_ADMIN_NAME sslmode=require" -v ON_ERROR_STOP=1 -q -c $grantSql
            if ($LASTEXITCODE -eq 0) {
                $registered = $true
                break
            }
        }

        if ($attempt -lt 10) {
            Write-Host "PostgreSQL role registration failed; retrying (attempt $attempt/10)."
            Start-Sleep -Seconds 15
        }
    }

    if (-not $registered) {
        throw 'Unable to register the Function identity as a PostgreSQL role.'
    }
    Write-Host "Registered '$functionName' as a PostgreSQL Microsoft Entra principal."
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    & az postgres flexible-server firewall-rule delete @firewallArguments --yes
}