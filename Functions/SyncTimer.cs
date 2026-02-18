namespace AnalyticsAPI.Sync.Functions;

using AnalyticsAPI.Sync.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

public class SyncTimer
{
    private readonly ILogger<SyncTimer> _logger;
    private readonly TenantService _tenantService;
    private readonly TokenService _tokenService;
    private readonly BCApiService _bcApiService;
    private readonly SqlService _sqlService;

    public SyncTimer(
        ILogger<SyncTimer> logger,
        TenantService tenantService,
        TokenService tokenService,
        BCApiService bcApiService,
        SqlService sqlService)
    {
        _logger = logger;
        _tenantService = tenantService;
        _tokenService = tokenService;
        _bcApiService = bcApiService;
        _sqlService = sqlService;
    }

    [Function("SyncTimer")]
    public async Task Run([TimerTrigger("0 */30 * * * *")] TimerInfo myTimer)
    {
        _logger.LogInformation("Sync started at: {Time}", DateTime.Now);

        var environments = await _tenantService.GetActiveEnvironmentsAsync();
        _logger.LogInformation("Found {Count} active environments to sync", environments.Count);

        foreach (var env in environments)
        {
            _logger.LogInformation("Syncing {Company} in {Env}", env.CompanyName, env.EnvironmentName);

            await _tenantService.LogSyncStartAsync(env.TenantId, env.EnvironmentId);

            int totalRecords = 0;

            try
            {
                totalRecords += await SyncGLAccountsAsync(env);
                totalRecords += await SyncGLEntriesAsync(env);
                totalRecords += await SyncDimensionSetEntriesAsync(env);
                totalRecords += await SyncGLBudgetEntriesAsync(env);

                await _tenantService.LogSyncCompleteAsync(env.TenantId, env.EnvironmentId, totalRecords, "Success");
                _logger.LogInformation("Completed sync for {Company}: {Records} records", env.CompanyName, totalRecords);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed for {Company}", env.CompanyName);
                await _tenantService.LogSyncCompleteAsync(env.TenantId, env.EnvironmentId, totalRecords, "Failed", ex.Message);
            }
        }

        _logger.LogInformation("Sync completed at: {Time}", DateTime.Now);
    }

    private async Task<int> SyncGLAccountsAsync(TenantEnvironment env)
    {
        try
        {
            _logger.LogInformation("Syncing GL Accounts...");
            
            var connectionString = env.GetConnectionString();
            var lastSync = await _sqlService.GetLastSyncAsync(connectionString, env.EnvironmentName, "dim_Account");
            
            var token = await _tokenService.GetTokenAsync(env.BCTenantId, env.AzureADClientId);
            var accounts = await _bcApiService.GetAllAsync<Models.GLAccount>(
                token, 
                env.BCTenantId, 
                env.EnvironmentName, 
                env.CompanyId, 
                "glAccounts", 
                lastSync);

            if (accounts.Count == 0)
            {
                _logger.LogInformation("No new GL Accounts to sync");
                return 0;
            }

            await _sqlService.UpsertGLAccountsAsync(connectionString, env.EnvironmentName, env.CompanyId, accounts);
            await _sqlService.UpdateSyncLogAsync(connectionString, env.EnvironmentName, "dim_Account", accounts.Count, "Success");
            
            _logger.LogInformation("GL Accounts sync complete: {Count} records", accounts.Count);
            return accounts.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Accounts sync failed");
            await _sqlService.UpdateSyncLogAsync(env.GetConnectionString(), env.EnvironmentName, "dim_Account", 0, "Failed", ex.Message);
            throw;
        }
    }

    private async Task<int> SyncGLEntriesAsync(TenantEnvironment env)
    {
        try
        {
            _logger.LogInformation("Syncing GL Entries...");
            
            var connectionString = env.GetConnectionString();
            var lastSync = await _sqlService.GetLastSyncAsync(connectionString, env.EnvironmentName, "fact_GL");
            
            var token = await _tokenService.GetTokenAsync(env.BCTenantId, env.AzureADClientId);
            var entries = await _bcApiService.GetAllAsync<Models.GLEntry>(
                token, 
                env.BCTenantId, 
                env.EnvironmentName, 
                env.CompanyId, 
                "glEntries", 
                lastSync);

            if (entries.Count == 0)
            {
                _logger.LogInformation("No new GL Entries to sync");
                return 0;
            }

            await _sqlService.UpsertGLEntriesAsync(connectionString, env.EnvironmentName, env.CompanyId, entries);
            await _sqlService.UpdateSyncLogAsync(connectionString, env.EnvironmentName, "fact_GL", entries.Count, "Success");
            
            _logger.LogInformation("GL Entries sync complete: {Count} records", entries.Count);
            return entries.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Entries sync failed");
            await _sqlService.UpdateSyncLogAsync(env.GetConnectionString(), env.EnvironmentName, "fact_GL", 0, "Failed", ex.Message);
            throw;
        }
    }

    private async Task<int> SyncDimensionSetEntriesAsync(TenantEnvironment env)
    {
        try
        {
            _logger.LogInformation("Syncing Dimension Set Entries...");
            
            var connectionString = env.GetConnectionString();
            var lastSync = await _sqlService.GetLastSyncAsync(connectionString, env.EnvironmentName, "dim_Dimension");
            
            var token = await _tokenService.GetTokenAsync(env.BCTenantId, env.AzureADClientId);
            var dimensions = await _bcApiService.GetAllAsync<Models.DimensionSetEntry>(
                token, 
                env.BCTenantId, 
                env.EnvironmentName, 
                env.CompanyId, 
                "dimensionSetEntries", 
                lastSync);

            if (dimensions.Count == 0)
            {
                _logger.LogInformation("No new Dimension Set Entries to sync");
                return 0;
            }

            await _sqlService.UpsertDimensionSetEntriesAsync(connectionString, env.EnvironmentName, env.CompanyId, dimensions);
            await _sqlService.UpdateSyncLogAsync(connectionString, env.EnvironmentName, "dim_Dimension", dimensions.Count, "Success");
            
            _logger.LogInformation("Dimension Set Entries sync complete: {Count} records", dimensions.Count);
            return dimensions.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dimension Set Entries sync failed");
            await _sqlService.UpdateSyncLogAsync(env.GetConnectionString(), env.EnvironmentName, "dim_Dimension", 0, "Failed", ex.Message);
            throw;
        }
    }

    private async Task<int> SyncGLBudgetEntriesAsync(TenantEnvironment env)
    {
        try
        {
            _logger.LogInformation("Syncing GL Budget Entries...");
            
            var connectionString = env.GetConnectionString();
            var lastSync = await _sqlService.GetLastSyncAsync(connectionString, env.EnvironmentName, "fact_Budget");
            
            var token = await _tokenService.GetTokenAsync(env.BCTenantId, env.AzureADClientId);
            var budgetEntries = await _bcApiService.GetAllAsync<Models.GLBudgetEntry>(
                token, 
                env.BCTenantId, 
                env.EnvironmentName, 
                env.CompanyId, 
                "glBudgetEntries", 
                lastSync);

            if (budgetEntries.Count == 0)
            {
                _logger.LogInformation("No new GL Budget Entries to sync");
                return 0;
            }

            await _sqlService.UpsertGLBudgetEntriesAsync(connectionString, env.EnvironmentName, env.CompanyId, budgetEntries);
            await _sqlService.UpdateSyncLogAsync(connectionString, env.EnvironmentName, "fact_Budget", budgetEntries.Count, "Success");
            
            _logger.LogInformation("GL Budget Entries sync complete: {Count} records", budgetEntries.Count);
            return budgetEntries.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Budget Entries sync failed");
            await _sqlService.UpdateSyncLogAsync(env.GetConnectionString(), env.EnvironmentName, "fact_Budget", 0, "Failed", ex.Message);
            throw;
        }
    }
}