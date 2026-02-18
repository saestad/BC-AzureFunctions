namespace AnalyticsAPI.Sync.Services;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

public class TenantService
{
    private readonly string _controlConnectionString;
    private readonly ILogger<TenantService> _logger;

    public TenantService(ILogger<TenantService> logger)
    {
        _controlConnectionString = Environment.GetEnvironmentVariable("CONTROL_DB_CONNECTION_STRING")
            ?? throw new Exception("CONTROL_DB_CONNECTION_STRING not configured");
        _logger = logger;
    }

    public async Task<List<TenantEnvironment>> GetActiveEnvironmentsAsync()
    {
        var environments = new List<TenantEnvironment>();

        using var conn = new SqlConnection(_controlConnectionString);
        await conn.OpenAsync();

        var cmd = new SqlCommand(@"
            SELECT 
                te.EnvironmentId,
                te.TenantId,
                t.DatabaseServer,
                t.DatabaseName,
                te.EnvironmentName,
                te.BCTenantId,
                te.CompanyId,
                te.CompanyName,
                te.AzureADClientId
            FROM dbo.TenantEnvironments te
            INNER JOIN dbo.Tenants t ON te.TenantId = t.TenantId
            WHERE t.IsActive = 1 AND te.IsActive = 1", conn);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            environments.Add(new TenantEnvironment
            {
                EnvironmentId = reader.GetGuid(0),
                TenantId = reader.GetGuid(1),
                DatabaseServer = reader.GetString(2),
                DatabaseName = reader.GetString(3),
                EnvironmentName = reader.GetString(4),
                BCTenantId = reader.GetGuid(5),
                CompanyId = reader.GetGuid(6),
                CompanyName = reader.GetString(7),
                AzureADClientId = reader.GetGuid(8)
            });
        }

        return environments;
    }

    public async Task LogSyncStartAsync(Guid tenantId, Guid environmentId)
    {
        using var conn = new SqlConnection(_controlConnectionString);
        await conn.OpenAsync();

        var cmd = new SqlCommand(@"
            INSERT INTO dbo.SyncHistory (TenantId, EnvironmentId, SyncStarted, Status)
            VALUES (@TenantId, @EnvironmentId, @SyncStarted, 'InProgress')", conn);

        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@EnvironmentId", environmentId);
        cmd.Parameters.AddWithValue("@SyncStarted", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task LogSyncCompleteAsync(Guid tenantId, Guid environmentId, int recordsSynced, string status, string? error = null)
    {
        using var conn = new SqlConnection(_controlConnectionString);
        await conn.OpenAsync();

        var cmd = new SqlCommand(@"
            UPDATE dbo.SyncHistory 
            SET SyncCompleted = @SyncCompleted,
                RecordsSynced = @RecordsSynced,
                Status = @Status,
                ErrorMessage = @Error
            WHERE TenantId = @TenantId 
                AND EnvironmentId = @EnvironmentId 
                AND SyncCompleted IS NULL", conn);

        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@EnvironmentId", environmentId);
        cmd.Parameters.AddWithValue("@SyncCompleted", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@RecordsSynced", recordsSynced);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }
}

public class TenantEnvironment
{
    public Guid EnvironmentId { get; set; }
    public Guid TenantId { get; set; }
    public string DatabaseServer { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public Guid BCTenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public Guid AzureADClientId { get; set; }
    
    public string GetConnectionString()
    {
        var user = Environment.GetEnvironmentVariable("SQL_USER") ?? throw new Exception("SQL_USER not configured");
        var password = Environment.GetEnvironmentVariable("SQL_PASSWORD") ?? throw new Exception("SQL_PASSWORD not configured");
        
        return $"Server={DatabaseServer};Database={DatabaseName};User Id={user};Password={password};TrustServerCertificate=True;";
    }
}