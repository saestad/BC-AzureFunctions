namespace AnalyticsAPI.Sync.Functions;

using AnalyticsAPI.Sync.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

public class SyncTimer
{
    private readonly BCApiService _bcApiService;
    private readonly SqlService _sqlService;
    private readonly ILogger<SyncTimer> _logger;

    public SyncTimer(BCApiService bcApiService, SqlService sqlService, ILogger<SyncTimer> logger)
    {
        _bcApiService = bcApiService;
        _sqlService = sqlService;
        _logger = logger;
    }

    [Function("SyncTimer")]
    public async Task Run([TimerTrigger("0 */30 * * * *")] TimerInfo myTimer)
    {
        _logger.LogInformation("Sync started at: {Time}", DateTime.UtcNow);

        await SyncGLAccountsAsync();
        await SyncGLEntriesAsync();
        await SyncDimensionSetEntriesAsync();
        await SyncGLBudgetEntriesAsync();

        _logger.LogInformation("Sync completed at: {Time}", DateTime.UtcNow);
    }

    private async Task SyncGLAccountsAsync()
    {
        try
        {
            _logger.LogInformation("Syncing GL Accounts...");
            var lastSync = await _sqlService.GetLastSyncAsync("dim_Account");
            var accounts = await _bcApiService.GetAllAsync<AnalyticsAPI.Sync.Models.GLAccount>("glAccounts", lastSync);

            if (accounts.Count == 0)
            {
                _logger.LogInformation("No new GL Accounts to sync");
                return;
            }

            await _sqlService.UpsertGLAccountsAsync(accounts);
            await _sqlService.UpdateSyncLogAsync("dim_Account", accounts.Count, "Success");
            _logger.LogInformation("GL Accounts sync complete: {Count} records", accounts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Accounts sync failed");
            await _sqlService.UpdateSyncLogAsync("dim_Account", 0, "Failed", ex.Message);
        }
    }

    private async Task SyncGLEntriesAsync()
    {
        try
        {
            _logger.LogInformation("Syncing GL Entries...");
            var lastSync = await _sqlService.GetLastSyncAsync("fact_GL");
            var entries = await _bcApiService.GetAllAsync<AnalyticsAPI.Sync.Models.GLEntry>("glEntries", lastSync);

            if (entries.Count == 0)
            {
                _logger.LogInformation("No new GL Entries to sync");
                return;
            }

            await _sqlService.UpsertGLEntriesAsync(entries);
            await _sqlService.UpdateSyncLogAsync("fact_GL", entries.Count, "Success");
            _logger.LogInformation("GL Entries sync complete: {Count} records", entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Entries sync failed");
            await _sqlService.UpdateSyncLogAsync("fact_GL", 0, "Failed", ex.Message);
        }
    }

    private async Task SyncDimensionSetEntriesAsync()
    {
        try
        {
            _logger.LogInformation("Syncing Dimension Set Entries...");
            var lastSync = await _sqlService.GetLastSyncAsync("dim_Dimension");
            var dimensions = await _bcApiService.GetAllAsync<AnalyticsAPI.Sync.Models.DimensionSetEntry>("dimensionSetEntries", lastSync);

            if (dimensions.Count == 0)
            {
                _logger.LogInformation("No new Dimension Set Entries to sync");
                return;
            }

            await _sqlService.UpsertDimensionSetEntriesAsync(dimensions);
            await _sqlService.UpdateSyncLogAsync("dim_Dimension", dimensions.Count, "Success");
            _logger.LogInformation("Dimension Set Entries sync complete: {Count} records", dimensions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dimension Set Entries sync failed");
            await _sqlService.UpdateSyncLogAsync("dim_Dimension", 0, "Failed", ex.Message);
        }
    }

    private async Task SyncGLBudgetEntriesAsync()
    {
        try
        {
            _logger.LogInformation("Syncing GL Budget Entries...");
            var lastSync = await _sqlService.GetLastSyncAsync("fact_Budget");
            var budgetEntries = await _bcApiService.GetAllAsync<AnalyticsAPI.Sync.Models.GLBudgetEntry>("glBudgetEntries", lastSync);

            if (budgetEntries.Count == 0)
            {
                _logger.LogInformation("No new GL Budget Entries to sync");
                return;
            }

            await _sqlService.UpsertGLBudgetEntriesAsync(budgetEntries);
            await _sqlService.UpdateSyncLogAsync("fact_Budget", budgetEntries.Count, "Success");
            _logger.LogInformation("GL Budget Entries sync complete: {Count} records", budgetEntries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Budget Entries sync failed");
            await _sqlService.UpdateSyncLogAsync("fact_Budget", 0, "Failed", ex.Message);
        }
    }
}