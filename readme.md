# Business Central Azure Functions

A multi-tenant C# Azure Functions project that syncs data from Business Central custom API endpoints into Azure SQL on a schedule. Part of the Analytics API by Saestad SaaS product.

## Overview

This function connects to BC API endpoints exposed by the [BC-AnalyticsAPI](https://github.com/saestad/BC-AnalyticsAPI) AL extension, pulls data incrementally using `lastModifiedDateTime`, and upserts it into isolated per-tenant Azure SQL databases. Power BI then connects to Azure SQL instead of BC directly.
```
BC Custom API (AL Extension)
        ↓
Azure Function (this project)
        ↓
Control Database (tenant metadata)
        ↓
Per-Tenant Databases (isolated data)
        ↓
Power BI (per customer)
```

## Multi-Tenant Architecture

**Control Plane Database:** `analyticsapi-control`
- Tracks all customer tenants and their BC environments
- Stores metadata (tenant names, database locations, company mappings)
- Logs sync history across all tenants

**Per-Tenant Databases:** `analyticsapi-{tenantslug}`
- One isolated database per customer
- Contains all their BC environments and companies
- Data distinguished by `EnvironmentName` and `CompanyId` columns

**Example Structure:**
```
SQL Server: {yourserver}.database.windows.net
├─ analyticsapi-control (metadata)
├─ analyticsapi-acme (Acme Corp's data)
│   └─ ProductionNO, ProductionUS, ProductionSE environments
├─ analyticsapi-contoso (Contoso Ltd's data)
└─ analyticsapi-fabrikam (Fabrikam Inc's data)
```

## Endpoints Synced

| BC Endpoint | SQL Table | Description |
|---|---|---|
| `/glAccounts` | `analytics.dim_Account` | Chart of accounts |
| `/glEntries` | `analytics.fact_GL` | Posted GL transactions |
| `/dimensionSetEntries` | `analytics.dim_Dimension` | Dimension values |
| `/glBudgetEntries` | `analytics.fact_Budget` | Budget figures |

## Prerequisites

- .NET 8 SDK
- Azure Functions Core Tools v4
- Azure SQL Server with control database and tenant databases
- BC-AnalyticsAPI AL extension installed in customer BC environments
- Azure AD app registration with `API.ReadWrite.All` application permission granted for BC

## Setup

1. Clone the repo
```bash
git clone https://github.com/saestad/BC-AzureFunctions.git
cd BC-AzureFunctions
```

2. Copy the settings template
```bash
cp local.settings.json.example local.settings.json
```

3. Fill in your values in `local.settings.json`:
```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "UseDevelopmentStorage=true",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
        "FUNCTIONS_INPROC_NET8_ENABLED": "1",
        "BC_CLIENT_SECRET": "{CLIENT SECRET FROM AZURE AD APP REGISTRATION}",
        "SQL_USER": "{SQL SERVER USERNAME}",
        "SQL_PASSWORD": "{SQL SERVER PASSWORD}",
        "CONTROL_DB_CONNECTION_STRING": "Server={SERVER}.database.windows.net;Database=analyticsapi-control;User Id={USER};Password={PASSWORD};TrustServerCertificate=True;"
    }
}
```

`local.settings.json` is gitignored and will never be committed.

4. Install Azurite for local storage emulation
```
VS Code Extensions → search Azurite → Install
Ctrl+Shift+P → Azurite: Start
```

5. Build and run
```bash
dotnet build
func start
```

6. Trigger manually for testing
```powershell
Invoke-RestMethod -Uri "http://localhost:7071/admin/functions/SyncTimer" -Method Post -ContentType "application/json" -Body "{}"
```

## Azure SQL Schema

### Control Plane Database: `analyticsapi-control`
```sql
CREATE DATABASE [analyticsapi-control];
GO

USE [analyticsapi-control];
GO

CREATE TABLE dbo.Tenants (
    TenantId                UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TenantName              NVARCHAR(100) NOT NULL,
    TenantSlug              NVARCHAR(50) NOT NULL UNIQUE,
    SubscriptionTier        NVARCHAR(20),
    SubscriptionStatus      NVARCHAR(20) DEFAULT 'Active',
    DatabaseServer          NVARCHAR(100) NOT NULL,
    DatabaseName            NVARCHAR(100) NOT NULL,
    DatabaseTier            NVARCHAR(20),
    IsActive                BIT NOT NULL DEFAULT 1,
    CreatedDate             DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    BillingEmail            NVARCHAR(255),
    TechnicalContactEmail   NVARCHAR(255)
);

CREATE TABLE dbo.TenantEnvironments (
    EnvironmentId           UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TenantId                UNIQUEIDENTIFIER NOT NULL,
    EnvironmentName         NVARCHAR(50) NOT NULL,
    BCTenantId              UNIQUEIDENTIFIER NOT NULL,
    CompanyId               UNIQUEIDENTIFIER NOT NULL,
    CompanyName             NVARCHAR(100) NOT NULL,
    AzureADClientId         UNIQUEIDENTIFIER NOT NULL,
    ClientSecretKeyVaultUri NVARCHAR(200),
    IsActive                BIT NOT NULL DEFAULT 1,
    LastSyncDateTime        DATETIME2,
    CreatedDate             DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(TenantId)
);

CREATE TABLE dbo.SyncHistory (
    SyncId                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TenantId                UNIQUEIDENTIFIER NOT NULL,
    EnvironmentId           UNIQUEIDENTIFIER NOT NULL,
    SyncStarted             DATETIME2 NOT NULL,
    SyncCompleted           DATETIME2,
    RecordsSynced           INT,
    Status                  NVARCHAR(20),
    ErrorMessage            NVARCHAR(MAX),
    FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(TenantId),
    FOREIGN KEY (EnvironmentId) REFERENCES dbo.TenantEnvironments(EnvironmentId)
);
```

### Per-Tenant Database: `analyticsapi-{tenantslug}`
```sql
CREATE DATABASE [analyticsapi-acme]; -- Replace 'acme' with tenant slug
GO

USE [analyticsapi-acme];
GO

CREATE SCHEMA analytics;
GO

CREATE TABLE analytics.dim_Account (
    SystemId                    UNIQUEIDENTIFIER,
    EnvironmentName             NVARCHAR(50),
    CompanyId                   UNIQUEIDENTIFIER,
    No                          NVARCHAR(20),
    Name                        NVARCHAR(100),
    AccountType                 NVARCHAR(50),
    AccountCategory             NVARCHAR(50),
    AccountSubcategory          NVARCHAR(100),
    AccountSubcategoryEntryNo   INT,
    IncomeBalance               NVARCHAR(50),
    Indentation                 INT,
    Blocked                     BIT,
    LastModifiedDateTime        DATETIME2,
    PRIMARY KEY (SystemId, EnvironmentName, CompanyId)
);

CREATE TABLE analytics.fact_GL (
    SystemId                UNIQUEIDENTIFIER,
    EnvironmentName         NVARCHAR(50),
    CompanyId               UNIQUEIDENTIFIER,
    EntryNo                 INT,
    GLAccountNo             NVARCHAR(20),
    PostingDate             DATE,
    DocumentType            NVARCHAR(50),
    DocumentNo              NVARCHAR(50),
    Description             NVARCHAR(100),
    Amount                  DECIMAL(18,2),
    DebitAmount             DECIMAL(18,2),
    CreditAmount            DECIMAL(18,2),
    DimensionSetId          INT,
    LastModifiedDateTime    DATETIME2,
    PRIMARY KEY (SystemId, EnvironmentName, CompanyId)
);

CREATE TABLE analytics.dim_Dimension (
    SystemId                UNIQUEIDENTIFIER,
    EnvironmentName         NVARCHAR(50),
    CompanyId               UNIQUEIDENTIFIER,
    DimensionSetId          INT,
    DimensionCode           NVARCHAR(20),
    DimensionValueCode      NVARCHAR(20),
    DimensionValueName      NVARCHAR(100),
    LastModifiedDateTime    DATETIME2,
    PRIMARY KEY (SystemId, EnvironmentName, CompanyId)
);

CREATE TABLE analytics.fact_Budget (
    SystemId                UNIQUEIDENTIFIER,
    EnvironmentName         NVARCHAR(50),
    CompanyId               UNIQUEIDENTIFIER,
    EntryNo                 INT,
    BudgetName              NVARCHAR(50),
    GLAccountNo             NVARCHAR(20),
    Date                    DATE,
    Amount                  DECIMAL(18,2),
    Description             NVARCHAR(100),
    DimensionSetId          INT,
    LastModifiedDateTime    DATETIME2,
    PRIMARY KEY (SystemId, EnvironmentName, CompanyId)
);

CREATE TABLE analytics.SyncLog (
    EnvironmentName         NVARCHAR(50),
    TableName               NVARCHAR(50),
    LastSyncDateTime        DATETIME2,
    RowsSynced              INT,
    SyncStatus              NVARCHAR(20),
    LastError               NVARCHAR(MAX),
    PRIMARY KEY (EnvironmentName, TableName)
);

-- Seed SyncLog for initial sync
INSERT INTO analytics.SyncLog (EnvironmentName, TableName, LastSyncDateTime, RowsSynced, SyncStatus)
VALUES 
    ('Production', 'dim_Account', '2000-01-01', 0, 'Pending'),
    ('Production', 'fact_GL', '2000-01-01', 0, 'Pending'),
    ('Production', 'dim_Dimension', '2000-01-01', 0, 'Pending'),
    ('Production', 'fact_Budget', '2000-01-01', 0, 'Pending');
```

## Azure AD App Registration

Your app registration needs:

| Permission | Type | Status |
|---|---|---|
| Financials.ReadWrite.All | Delegated | Granted |
| API.ReadWrite.All | Application | Granted |

The Application permission is required for client credentials flow (no signed-in user).

Also add this redirect URI under Authentication:
```
https://businesscentral.dynamics.com/OAuthLanding.htm
```

## BC Setup

In each customer's BC environment, register the app under **Microsoft Entra Applications**:

| Field | Value |
|---|---|
| Client ID | Your Azure AD app client ID |
| State | Enabled |

Assign these permission sets to the app:

| Permission Set |
|---|
| Analytics API Access |
| D365 BASIC |

## Project Structure
```
BC-AzureFunctions/
├── Functions/
│   └── SyncTimer.cs              # Timer trigger - loops through all tenants
├── Models/
│   ├── GLAccount.cs
│   ├── GLEntry.cs
│   ├── DimensionSetEntry.cs
│   ├── GLBudgetEntry.cs
│   └── ODataResponse.cs
├── Services/
│   ├── TenantService.cs          # Reads tenant metadata from control DB
│   ├── TokenService.cs           # OAuth client credentials token handling
│   ├── BCApiService.cs           # Calls BC API endpoints with paging support
│   └── SqlService.cs             # SqlBulkCopy bulk insert + MERGE upsert
├── local.settings.json           # Local config - gitignored
├── local.settings.json.example   # Template - copy this to local.settings.json
└── .gitignore
```

## Sync Schedule

The timer runs every 30 minutes by default (`0 */30 * * * *`). Change the cron expression in `SyncTimer.cs` to adjust.

**Sync Process:**
1. Queries `analyticsapi-control` for all active tenant environments
2. For each tenant → environment → company:
   - Authenticates with BC using OAuth
   - Fetches data incrementally using `lastModifiedDateTime` filter
   - Bulk inserts into staging table using SqlBulkCopy
   - Merges from staging into target table
   - Updates SyncLog and SyncHistory
3. Logs success/failure in control database

To force a full re-sync for a specific environment:
```sql
USE [analyticsapi-acme];
GO

UPDATE analytics.SyncLog 
SET LastSyncDateTime = '2000-01-01'
WHERE EnvironmentName = 'Production';
```

## Deploying to Azure

### Create Function App

1. In Azure Portal, create a Function App:
   - Runtime: .NET 8 (Isolated)
   - Region: Same as your SQL Server
   - Plan: Consumption (Serverless)
   - Memory: 512 MB

### Configure Application Settings

In the Function App, go to **Configuration** → **Application settings** and add:

| Setting | Value |
|---|---|
| `BC_CLIENT_SECRET` | Your Azure AD app client secret |
| `SQL_USER` | SQL Server username |
| `SQL_PASSWORD` | SQL Server password |
| `CONTROL_DB_CONNECTION_STRING` | Full connection string to `analyticsapi-control` |
| `FUNCTIONS_INPROC_NET8_ENABLED` | `1` |

### Configure SQL Firewall

In your SQL Server → **Networking**:
- Toggle ON: **Allow Azure services and resources to access this server**
- Or add specific Function App outbound IPs to firewall rules

### Deploy from VS Code

1. Install Azure Functions extension
2. Sign in to Azure
3. Right-click your Function App → **Deploy to Function App**
4. Wait for deployment to complete

### Verify Deployment

- Go to Function App → **Functions** → **SyncTimer** → **Monitor**
- Check execution history for successful runs every 30 minutes
- Verify data in SQL:
```sql
  SELECT TOP 10 * FROM [analyticsapi-control].dbo.SyncHistory 
  ORDER BY SyncStarted DESC;
```

## Adding a New Customer Tenant
```sql
USE [analyticsapi-control];
GO

-- 1. Create tenant database
CREATE DATABASE [analyticsapi-newcustomer];
-- (Run per-tenant schema script above)

-- 2. Register tenant
INSERT INTO dbo.Tenants (TenantName, TenantSlug, DatabaseServer, DatabaseName, SubscriptionStatus)
VALUES ('New Customer Inc', 'newcustomer', '{yourserver}.database.windows.net', 'analyticsapi-newcustomer', 'Active');

-- 3. Add their BC environment(s)
DECLARE @TenantId UNIQUEIDENTIFIER = (SELECT TenantId FROM dbo.Tenants WHERE TenantSlug = 'newcustomer');

INSERT INTO dbo.TenantEnvironments (
    TenantId, EnvironmentName, BCTenantId, CompanyId, CompanyName, AzureADClientId
)
VALUES (
    @TenantId,
    'Production',
    '{customer BC tenant GUID}',
    '{customer company GUID}',
    'New Customer Inc',
    '{your Azure AD app client ID}'
);
```

Next sync cycle will automatically include the new tenant.

## Performance

Full sync of 4,096 records completes in ~4 seconds using SqlBulkCopy bulk insert pattern.

Typical production environment with 50,000 GL entries syncs in under 10 seconds.

## Security

- `local.settings.json` is gitignored - credentials never leave your machine
- Use `local.settings.json.example` as the setup template
- Store secrets in Azure Key Vault when deploying to production
- Per-tenant database isolation ensures complete data separation between customers

## Related

- [BC-AnalyticsAPI](https://github.com/saestad/BC-AnalyticsAPI) - the AL extension that exposes the BC API endpoints this function consumes

## Author

**Stein Saestad**
[saestad.no](https://saestad.no)

## License

Proprietary. All rights reserved. 2026 Stein Saestad

## Development Log

| Date | Hours | Work |
|---|---|---|
| 14.02.2026 - 15.02.2026 | 8h | Initial BC AL extension and single-tenant sync |
| 18.02.2026 | 6h | Multi-tenant architecture with control plane |