$ErrorActionPreference = 'Stop'

$requiredVariables = @(
    'AZURE_SUBSCRIPTION_ID',
    'AZURE_RESOURCE_GROUP',
    'AZURE_FUNCTION_NAME',
    'AZURE_WEB_APP_NAME'
)

foreach ($name in $requiredVariables) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Required azd environment variable '$name' is not set."
    }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is required to configure the Function key.'
}

$functionKey = $null
for ($attempt = 1; $attempt -le 20; $attempt++) {
    $keyOutput = & az functionapp keys list `
        --subscription $env:AZURE_SUBSCRIPTION_ID `
        --resource-group $env:AZURE_RESOURCE_GROUP `
        --name $env:AZURE_FUNCTION_NAME `
        --query 'functionKeys.default' `
        --output tsv `
        --only-show-errors
    $keyExitCode = $LASTEXITCODE
    $candidateKey = ($keyOutput -join '').Trim()

    if ($keyExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($candidateKey) -and $candidateKey -ne 'None') {
        $functionKey = $candidateKey
        break
    }

    if ($attempt -lt 20) {
        Write-Host "Waiting for the Function host key (attempt $attempt/20)."
        Start-Sleep -Seconds 15
    }
}

if ([string]::IsNullOrWhiteSpace($functionKey)) {
    throw 'The Function host key was not available within five minutes.'
}

& az webapp config appsettings set `
    --subscription $env:AZURE_SUBSCRIPTION_ID `
    --resource-group $env:AZURE_RESOURCE_GROUP `
    --name $env:AZURE_WEB_APP_NAME `
    --settings "FunctionKey=$functionKey" `
    --only-show-errors `
    --output none

if ($LASTEXITCODE -ne 0) {
    throw 'Unable to set the Function key in the App Service settings.'
}

Write-Host 'Configured the Function key in the App Service settings.'