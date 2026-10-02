#!/usr/bin/env sh
set -eu

required_variables='
AZURE_SUBSCRIPTION_ID
AZURE_RESOURCE_GROUP
AZURE_FUNCTION_NAME
AZURE_WEB_APP_NAME
'

for name in $required_variables; do
    eval "value=\${$name-}"
    if [ -z "$value" ]; then
        printf 'Required azd environment variable %s is not set.\n' "$name" >&2
        exit 1
    fi
done

if ! command -v az >/dev/null 2>&1; then
    printf 'Azure CLI (az) is required to configure the Function key.\n' >&2
    exit 1
fi

attempt=1
function_key=
while [ "$attempt" -le 20 ]; do
    if candidate_key=$(az functionapp keys list \
        --subscription "$AZURE_SUBSCRIPTION_ID" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --name "$AZURE_FUNCTION_NAME" \
        --query 'functionKeys.default' \
        --output tsv \
        --only-show-errors 2>/dev/null) &&
        [ -n "$candidate_key" ] && [ "$candidate_key" != None ]; then
        function_key=$candidate_key
        break
    fi

    if [ "$attempt" -lt 20 ]; then
        printf 'Waiting for the Function host key (attempt %s/20).\n' "$attempt"
        sleep 15
    fi
    attempt=$((attempt + 1))
done

if [ -z "$function_key" ]; then
    printf 'The Function host key was not available within five minutes.\n' >&2
    exit 1
fi

az webapp config appsettings set \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --name "$AZURE_WEB_APP_NAME" \
    --settings "FunctionKey=$function_key" \
    --only-show-errors \
    --output none

printf 'Configured the Function key in the App Service settings.\n'