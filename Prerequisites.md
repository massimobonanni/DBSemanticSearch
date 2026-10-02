# Deployment Prerequisites

Install and configure the following before running `azd up` from the repository root.

## Required tools

- Azure Developer CLI (`azd`)
- Azure CLI (`az`)
- .NET SDK 10
- PostgreSQL 16 command-line client (`psql`)

The deployment does not require a local PostgreSQL server. The `postprovision` hook uses `psql` to configure the Azure PostgreSQL server, authenticating with an Azure CLI access token rather than a PostgreSQL password.

### Install and configure `psql` on Windows

Install PostgreSQL 16, which includes `psql`:

```powershell
winget install --id PostgreSQL.PostgreSQL.16 --exact
```

Add the PostgreSQL `bin` directory to your **user** `Path` environment variable. The default path is:

```text
C:\Program Files\PostgreSQL\16\bin
```

If PostgreSQL was installed elsewhere, use that installation's `bin` directory. Close and reopen PowerShell or VS Code so the updated `Path` is loaded, then verify the client is available:

```powershell
psql --version
```

On macOS or Linux, install the PostgreSQL 16 client with the operating system's package manager and ensure `psql` is on `PATH`. The POSIX deployment hook also requires `curl`.

## Azure access and environment

- Sign in to both CLIs with an identity that can access the target subscription:

  ```powershell
  az login
  azd auth login
  ```

- The identity needs permission to create the resource group and its resources, and to create the role assignments used by the deployment. The post-provision hook also configures a Microsoft Entra administrator for Azure Database for PostgreSQL and registers the Function App identity.
- Choose an Azure region that supports Windows App Service, Flex Consumption, Azure Database for PostgreSQL Flexible Server, and the `text-embedding-3-large` deployment with sufficient quota.
- Create or select an AZD environment, set its location, and configure the PostgreSQL Entra administrator values. For the signed-in user, for example:

  ```powershell
  azd env new dev
  azd env set AZURE_LOCATION swedencentral
  $account = az ad signed-in-user show | ConvertFrom-Json
  azd env set AZURE_POSTGRES_ENTRA_ADMIN_OBJECT_ID $account.id
  azd env set AZURE_POSTGRES_ENTRA_ADMIN_NAME $account.userPrincipalName
  azd env set AZURE_POSTGRES_ENTRA_ADMIN_PRINCIPAL_TYPE User
  ```

  Confirm that the chosen region supports the model before using it. If deploying as a service principal or using a group as the PostgreSQL administrator, set `AZURE_POSTGRES_ENTRA_ADMIN_OBJECT_ID`, `AZURE_POSTGRES_ENTRA_ADMIN_NAME`, and `AZURE_POSTGRES_ENTRA_ADMIN_PRINCIPAL_TYPE` to that principal's values. Authenticate Azure CLI as the identity that will administer PostgreSQL.

AZD environment values are stored under `.azure/`, which is excluded from Git. The repository's post-provision hook temporarily adds a firewall rule for the deploying machine's public IP; it requires outbound access to `https://api.ipify.org` and removes the rule when it finishes.