# Business Central Azure Functions

A C# Azure Functions project that syncs data from Business Central custom API endpoints into Azure SQL on a schedule. Part of the Analytics API by Saestad product.

## Overview

This function connects to the BC API endpoints exposed by the [BC-AnalyticsAPI](https://github.com/saestad/BC-AnalyticsAPI) AL extension, pulls data incrementally using `lastModifiedDateTime`, and upserts it into an Azure SQL database. Power BI then connects to Azure SQL instead of BC directly.

```
BC Custom API (AL Extension)
        ↓
Azure Function (this project)
        ↓
Azure SQL Database (star schema)
        ↓
Power BI
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
- Azure SQL Database with the analytics schema created
- BC-AnalyticsAPI AL extension installed in your BC environment
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
        "BC_TENANT_ID": "{BC TENANT ID}",
        "BC_CLIENT_ID": "{CLIENT ID FROM AZURE AD APP REGISTRATION}",
        "BC_CLIENT_SECRET": "{CLIENT SECRET FROM AZURE AD APP REGISTRATION}",
        "BC_ENVIRONMENT": "{ENVIRONMENT NAME e.g. Sandbox_Env or Production}",
        "BC_COMPANY_ID": "{COMPANY ID IN BUSINESS CENTRAL}",
        "SQL_CONNECTION_STRING": "{SQL CONNECTION STRING}"
    }
}
```

`local.settings.json` is gitignored and will never be committed.

4. Install Azurite for local storage emulation
```
VS Code Extensions -> search Azurite -> Install
Ctrl+Shift+P -> Azurite: Start
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

Run this in SSMS or Azure Data Studio to create the required tables before the first sync:

```sql
CREATE SCHEMA analytics;
GO

CREATE TABLE analytics.dim_Account (
    SystemId                    UNIQUEIDENTIFIER PRIMARY KEY,
    No                          NVARCHAR(20),
    Name                        NVARCHAR(100),
    AccountType                 NVARCHAR(50),
    AccountCategory             NVARCHAR(50),
    AccountSubcategory          NVARCHAR(100),
    AccountSubcategoryEntryNo   INT,
    IncomeBalance               NVARCHAR(50),
    Indentation                 INT,
    Blocked                     BIT,
    LastModifiedDateTime        DATETIME2
);

CREATE TABLE analytics.fact_GL (
    SystemId                UNIQUEIDENTIFIER PRIMARY KEY,
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
    LastModifiedDateTime    DATETIME2
);

CREATE TABLE analytics.dim_Dimension (
    SystemId                UNIQUEIDENTIFIER PRIMARY KEY,
    DimensionSetId          INT,
    DimensionCode           NVARCHAR(20),
    DimensionValueCode      NVARCHAR(20),
    DimensionValueName      NVARCHAR(100),
    LastModifiedDateTime    DATETIME2
);

CREATE TABLE analytics.fact_Budget (
    SystemId                UNIQUEIDENTIFIER PRIMARY KEY,
    EntryNo                 INT,
    BudgetName              NVARCHAR(50),
    GLAccountNo             NVARCHAR(20),
    Date                    DATE,
    Amount                  DECIMAL(18,2),
    Description             NVARCHAR(100),
    DimensionSetId          INT,
    LastModifiedDateTime    DATETIME2
);

CREATE TABLE analytics.SyncLog (
    TableName               NVARCHAR(50) PRIMARY KEY,
    LastSyncDateTime        DATETIME2,
    RowsSynced              INT,
    SyncStatus              NVARCHAR(20),
    LastError               NVARCHAR(500)
);

INSERT INTO analytics.SyncLog (TableName, LastSyncDateTime, RowsSynced, SyncStatus)
VALUES 
    ('dim_Account', '2000-01-01', 0, 'Pending'),
    ('fact_GL', '2000-01-01', 0, 'Pending'),
    ('dim_Dimension', '2000-01-01', 0, 'Pending'),
    ('fact_Budget', '2000-01-01', 0, 'Pending');
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

In your BC environment, register the app under **Microsoft Entra Applications**:

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
│   └── SyncTimer.cs              # Timer trigger - runs every 30 minutes
├── Models/
│   ├── GLAccount.cs
│   ├── GLEntry.cs
│   ├── DimensionSetEntry.cs
│   ├── GLBudgetEntry.cs
│   └── ODataResponse.cs
├── Services/
│   ├── TokenService.cs           # OAuth client credentials token handling
│   ├── BCApiService.cs           # Calls BC API endpoints with paging support
│   └── SqlService.cs             # Upserts data into Azure SQL
├── local.settings.json           # Local config - gitignored
├── local.settings.json.example   # Template - copy this to local.settings.json
└── .gitignore
```

## Sync Schedule

The timer runs every 30 minutes by default (`0 */30 * * * *`). Change the cron expression in `SyncTimer.cs` to adjust.

Incremental refresh uses `lastModifiedDateTime` - only records changed since the last successful sync are pulled. The `analytics.SyncLog` table tracks the last sync time per table.

To force a full re-sync, reset the SyncLog in SSMS:

```sql
UPDATE analytics.SyncLog 
SET LastSyncDateTime = '2000-01-01';
```

## Security

- `local.settings.json` is gitignored - credentials never leave your machine
- Use `local.settings.json.example` as the setup template
- Store secrets in Azure Key Vault when deploying to production

## Related

- [BC-AnalyticsAPI](https://github.com/saestad/BC-AnalyticsAPI) - the AL extension that exposes the BC API endpoints this function consumes

## Author

**Stein Saestad**
[saestad.no](https://saestad.no)

## License

Proprietary. All rights reserved. 2026 Stein Saestad

## Hours spent on project
14.02.2026 - 15.02.2026 | 8 hours
