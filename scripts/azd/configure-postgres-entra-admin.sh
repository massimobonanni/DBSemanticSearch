#!/usr/bin/env sh
set -eu

required_variables='
AZURE_SUBSCRIPTION_ID
AZURE_RESOURCE_GROUP
AZURE_POSTGRES_SERVER_NAME
AZURE_POSTGRES_ENTRA_ADMIN_OBJECT_ID
AZURE_POSTGRES_ENTRA_ADMIN_NAME
AZURE_POSTGRES_ENTRA_ADMIN_PRINCIPAL_TYPE
'

for name in $required_variables; do
    eval "value=\${$name-}"
    if [ -z "$value" ]; then
        printf 'Required azd environment variable %s is not set.\n' "$name" >&2
        exit 1
    fi
done

if ! command -v az >/dev/null 2>&1; then
    printf 'Azure CLI (az) is required by the PostgreSQL Entra administrator hook.\n' >&2
    exit 1
fi

attempt=1
server_ready=false
while [ "$attempt" -le 24 ]; do
    if state=$(az postgres flexible-server show \
        --subscription "$AZURE_SUBSCRIPTION_ID" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --name "$AZURE_POSTGRES_SERVER_NAME" \
        --query state --output tsv --only-show-errors 2>/dev/null); then
        if [ "$state" = Ready ]; then
            server_ready=true
            break
        fi
        if [ "$state" = Failed ]; then
            printf 'PostgreSQL Flexible Server entered the Failed state.\n' >&2
            exit 1
        fi
    else
        state=unavailable
    fi

    if [ "$attempt" -lt 24 ]; then
        printf 'Waiting for PostgreSQL Flexible Server readiness (attempt %s/24; state: %s).\n' "$attempt" "$state"
        sleep 15
    fi
    attempt=$((attempt + 1))
done

if [ "$server_ready" != true ]; then
    printf 'PostgreSQL Flexible Server did not become Ready within six minutes.\n' >&2
    exit 1
fi

template_path="$(dirname "$0")/../../infra/postgres-entra-admin.bicep"
attempt=1
while [ "$attempt" -le 5 ]; do
    if az deployment group create \
        --name postgres-entra-admin \
        --subscription "$AZURE_SUBSCRIPTION_ID" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --template-file "$template_path" \
        --parameters \
            "serverName=$AZURE_POSTGRES_SERVER_NAME" \
            "entraAdminObjectId=$AZURE_POSTGRES_ENTRA_ADMIN_OBJECT_ID" \
            "entraAdminName=$AZURE_POSTGRES_ENTRA_ADMIN_NAME" \
            "entraAdminPrincipalType=$AZURE_POSTGRES_ENTRA_ADMIN_PRINCIPAL_TYPE" \
        --only-show-errors --output none; then
        printf 'Configured the PostgreSQL Microsoft Entra administrator.\n'
        exit 0
    fi

    if [ "$attempt" -lt 5 ]; then
        printf 'Administrator configuration failed; retrying (attempt %s/5).\n' "$attempt"
        sleep 15
    fi
    attempt=$((attempt + 1))
done

printf 'Unable to configure the PostgreSQL Microsoft Entra administrator after five attempts.\n' >&2
exit 1