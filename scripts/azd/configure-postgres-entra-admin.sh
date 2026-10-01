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
admin_configured=false
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
        admin_configured=true
        break
    fi

    if [ "$attempt" -lt 5 ]; then
        printf 'Administrator configuration failed; retrying (attempt %s/5).\n' "$attempt"
        sleep 15
    fi
    attempt=$((attempt + 1))
done

if [ "$admin_configured" != true ]; then
    printf 'Unable to configure the PostgreSQL Microsoft Entra administrator after five attempts.\n' >&2
    exit 1
fi

for name in AZURE_POSTGRES_HOST AZURE_FUNCTION_NAME AZURE_FUNCTION_PRINCIPAL_ID; do
    eval "value=\${$name-}"
    if [ -z "$value" ]; then
        printf 'Required azd environment variable %s is not set.\n' "$name" >&2
        exit 1
    fi
done

if ! command -v psql >/dev/null 2>&1; then
    printf 'psql is required to register the Function identity in PostgreSQL.\n' >&2
    exit 1
fi

# Values are interpolated into SQL, so accept only the formats Azure generates.
function_name="$AZURE_FUNCTION_NAME"
function_principal_id="$AZURE_FUNCTION_PRINCIPAL_ID"
if ! printf '%s' "$function_name" | grep -Eq '^[a-z0-9-]+$' ||
    ! printf '%s' "$function_principal_id" | grep -Eq '^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$'; then
    printf 'AZURE_FUNCTION_NAME or AZURE_FUNCTION_PRINCIPAL_ID has an unexpected format.\n' >&2
    exit 1
fi

database_name=semantic_search
firewall_rule_name=azd-postprovision-client

client_ip=$(curl -fsS https://api.ipify.org)
if ! printf '%s' "$client_ip" | grep -Eq '^[0-9]{1,3}(\.[0-9]{1,3}){3}$'; then
    printf 'Unable to determine the public IP address of this machine.\n' >&2
    exit 1
fi

az postgres flexible-server firewall-rule create \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --name "$AZURE_POSTGRES_SERVER_NAME" \
    --rule-name "$firewall_rule_name" \
    --start-ip-address "$client_ip" --end-ip-address "$client_ip" \
    --only-show-errors --output none

cleanup() {
    unset PGPASSWORD
    az postgres flexible-server firewall-rule delete \
        --subscription "$AZURE_SUBSCRIPTION_ID" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --name "$AZURE_POSTGRES_SERVER_NAME" \
        --rule-name "$firewall_rule_name" \
        --yes --only-show-errors --output none || true
}
trap cleanup EXIT

create_role_sql="SELECT pg_catalog.pgaadauth_create_principal_with_oid('$function_name', '$function_principal_id', 'service', false, false) WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '$function_name');"
grant_sql="CREATE EXTENSION IF NOT EXISTS vector; GRANT USAGE, CREATE ON SCHEMA public TO \"$function_name\";"

attempt=1
while [ "$attempt" -le 10 ]; do
    PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken --output tsv)
    export PGPASSWORD
    if psql "host=$AZURE_POSTGRES_HOST dbname=postgres user=$AZURE_POSTGRES_ENTRA_ADMIN_NAME sslmode=require" \
            -v ON_ERROR_STOP=1 -q -c "$create_role_sql" &&
        psql "host=$AZURE_POSTGRES_HOST dbname=$database_name user=$AZURE_POSTGRES_ENTRA_ADMIN_NAME sslmode=require" \
            -v ON_ERROR_STOP=1 -q -c "$grant_sql"; then
        printf "Registered '%s' as a PostgreSQL Microsoft Entra principal.\n" "$function_name"
        exit 0
    fi

    if [ "$attempt" -lt 10 ]; then
        printf 'PostgreSQL role registration failed; retrying (attempt %s/10).\n' "$attempt"
        sleep 15
    fi
    attempt=$((attempt + 1))
done

printf 'Unable to register the Function identity as a PostgreSQL role.\n' >&2
exit 1