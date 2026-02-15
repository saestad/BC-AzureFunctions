namespace AnalyticsAPI.Sync.Services;

using AnalyticsAPI.Sync.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

public class SqlService
{
    private readonly string _connectionString;
    private readonly ILogger<SqlService> _logger;

    public SqlService(ILogger<SqlService> logger)
    {
        _connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
            ?? throw new Exception("SQL_CONNECTION_STRING not configured");
        _logger = logger;
    }

    private SqlConnection GetConnection() => new SqlConnection(_connectionString);

    public async Task<DateTime> GetLastSyncAsync(string tableName)
    {
        using var conn = GetConnection();
        await conn.OpenAsync();

        var cmd = new SqlCommand(
            "SELECT LastSyncDateTime FROM analytics.SyncLog WHERE TableName = @TableName",
            conn);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        var result = await cmd.ExecuteScalarAsync();
        return result != null ? (DateTime)result : new DateTime(2000, 1, 1);
    }

    public async Task UpdateSyncLogAsync(string tableName, int rowsSynced, string status, string? error = null)
    {
        using var conn = GetConnection();
        await conn.OpenAsync();

        var cmd = new SqlCommand(@"
            UPDATE analytics.SyncLog 
            SET LastSyncDateTime = @LastSync,
                RowsSynced = @RowsSynced,
                SyncStatus = @Status,
                LastError = @Error
            WHERE TableName = @TableName", conn);

        cmd.Parameters.AddWithValue("@LastSync", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@RowsSynced", rowsSynced);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertGLAccountsAsync(List<GLAccount> accounts)
    {
        var dt = new DataTable();
        dt.Columns.Add("SystemId", typeof(Guid));
        dt.Columns.Add("No", typeof(string));
        dt.Columns.Add("Name", typeof(string));
        dt.Columns.Add("AccountType", typeof(string));
        dt.Columns.Add("AccountCategory", typeof(string));
        dt.Columns.Add("AccountSubcategory", typeof(string));
        dt.Columns.Add("AccountSubcategoryEntryNo", typeof(int));
        dt.Columns.Add("IncomeBalance", typeof(string));
        dt.Columns.Add("Indentation", typeof(int));
        dt.Columns.Add("Blocked", typeof(bool));
        dt.Columns.Add("LastModifiedDateTime", typeof(DateTime));

        foreach (var a in accounts)
            dt.Rows.Add(a.SystemId, a.No, a.Name, a.AccountType, a.AccountCategory,
                a.AccountSubcategory, a.AccountSubcategoryEntryNo, a.IncomeBalance,
                a.Indentation, a.Blocked, a.LastModifiedDateTime);

        using var conn = GetConnection();
        await conn.OpenAsync();

        await BulkInsertStagingAsync(conn, dt, "#staging_Account");

        await new SqlCommand(@"
            MERGE analytics.dim_Account AS target
            USING #staging_Account AS source ON target.SystemId = source.SystemId
            WHEN MATCHED THEN UPDATE SET
                No = source.No, Name = source.Name, AccountType = source.AccountType,
                AccountCategory = source.AccountCategory, AccountSubcategory = source.AccountSubcategory,
                AccountSubcategoryEntryNo = source.AccountSubcategoryEntryNo,
                IncomeBalance = source.IncomeBalance, Indentation = source.Indentation,
                Blocked = source.Blocked, LastModifiedDateTime = source.LastModifiedDateTime
            WHEN NOT MATCHED THEN INSERT
                (SystemId, No, Name, AccountType, AccountCategory, AccountSubcategory,
                 AccountSubcategoryEntryNo, IncomeBalance, Indentation, Blocked, LastModifiedDateTime)
            VALUES
                (source.SystemId, source.No, source.Name, source.AccountType, source.AccountCategory,
                 source.AccountSubcategory, source.AccountSubcategoryEntryNo, source.IncomeBalance,
                 source.Indentation, source.Blocked, source.LastModifiedDateTime);
            DROP TABLE #staging_Account;", conn).ExecuteNonQueryAsync();

        _logger.LogInformation("Upserted {Count} GL Accounts", accounts.Count);
    }

    public async Task UpsertGLEntriesAsync(List<GLEntry> entries)
    {
        var dt = new DataTable();
        dt.Columns.Add("SystemId", typeof(Guid));
        dt.Columns.Add("EntryNo", typeof(int));
        dt.Columns.Add("GLAccountNo", typeof(string));
        dt.Columns.Add("PostingDate", typeof(DateTime));
        dt.Columns.Add("DocumentType", typeof(string));
        dt.Columns.Add("DocumentNo", typeof(string));
        dt.Columns.Add("Description", typeof(string));
        dt.Columns.Add("Amount", typeof(decimal));
        dt.Columns.Add("DebitAmount", typeof(decimal));
        dt.Columns.Add("CreditAmount", typeof(decimal));
        dt.Columns.Add("DimensionSetId", typeof(int));
        dt.Columns.Add("LastModifiedDateTime", typeof(DateTime));

        foreach (var e in entries)
            dt.Rows.Add(e.SystemId, e.EntryNo, e.GLAccountNo, e.PostingDate, e.DocumentType,
                e.DocumentNo, e.Description, e.Amount, e.DebitAmount, e.CreditAmount,
                e.DimensionSetId, e.LastModifiedDateTime);

        using var conn = GetConnection();
        await conn.OpenAsync();

        await BulkInsertStagingAsync(conn, dt, "#staging_GL");

        await new SqlCommand(@"
            MERGE analytics.fact_GL AS target
            USING #staging_GL AS source ON target.SystemId = source.SystemId
            WHEN MATCHED THEN UPDATE SET
                EntryNo = source.EntryNo, GLAccountNo = source.GLAccountNo,
                PostingDate = source.PostingDate, DocumentType = source.DocumentType,
                DocumentNo = source.DocumentNo, Description = source.Description,
                Amount = source.Amount, DebitAmount = source.DebitAmount,
                CreditAmount = source.CreditAmount, DimensionSetId = source.DimensionSetId,
                LastModifiedDateTime = source.LastModifiedDateTime
            WHEN NOT MATCHED THEN INSERT
                (SystemId, EntryNo, GLAccountNo, PostingDate, DocumentType, DocumentNo,
                 Description, Amount, DebitAmount, CreditAmount, DimensionSetId, LastModifiedDateTime)
            VALUES
                (source.SystemId, source.EntryNo, source.GLAccountNo, source.PostingDate,
                 source.DocumentType, source.DocumentNo, source.Description, source.Amount,
                 source.DebitAmount, source.CreditAmount, source.DimensionSetId, source.LastModifiedDateTime);
            DROP TABLE #staging_GL;", conn).ExecuteNonQueryAsync();

        _logger.LogInformation("Upserted {Count} GL Entries", entries.Count);
    }

    public async Task UpsertDimensionSetEntriesAsync(List<DimensionSetEntry> dimensions)
    {
        var dt = new DataTable();
        dt.Columns.Add("SystemId", typeof(Guid));
        dt.Columns.Add("DimensionSetId", typeof(int));
        dt.Columns.Add("DimensionCode", typeof(string));
        dt.Columns.Add("DimensionValueCode", typeof(string));
        dt.Columns.Add("DimensionValueName", typeof(string));
        dt.Columns.Add("LastModifiedDateTime", typeof(DateTime));

        foreach (var d in dimensions)
            dt.Rows.Add(d.SystemId, d.DimensionSetId, d.DimensionCode,
                d.DimensionValueCode, d.DimensionValueName, d.LastModifiedDateTime);

        using var conn = GetConnection();
        await conn.OpenAsync();

        await BulkInsertStagingAsync(conn, dt, "#staging_Dimension");

        await new SqlCommand(@"
            MERGE analytics.dim_Dimension AS target
            USING #staging_Dimension AS source ON target.SystemId = source.SystemId
            WHEN MATCHED THEN UPDATE SET
                DimensionSetId = source.DimensionSetId, DimensionCode = source.DimensionCode,
                DimensionValueCode = source.DimensionValueCode,
                DimensionValueName = source.DimensionValueName,
                LastModifiedDateTime = source.LastModifiedDateTime
            WHEN NOT MATCHED THEN INSERT
                (SystemId, DimensionSetId, DimensionCode, DimensionValueCode,
                 DimensionValueName, LastModifiedDateTime)
            VALUES
                (source.SystemId, source.DimensionSetId, source.DimensionCode,
                 source.DimensionValueCode, source.DimensionValueName, source.LastModifiedDateTime);
            DROP TABLE #staging_Dimension;", conn).ExecuteNonQueryAsync();

        _logger.LogInformation("Upserted {Count} Dimension Set Entries", dimensions.Count);
    }

    public async Task UpsertGLBudgetEntriesAsync(List<GLBudgetEntry> budgetEntries)
    {
        var dt = new DataTable();
        dt.Columns.Add("SystemId", typeof(Guid));
        dt.Columns.Add("EntryNo", typeof(int));
        dt.Columns.Add("BudgetName", typeof(string));
        dt.Columns.Add("GLAccountNo", typeof(string));
        dt.Columns.Add("Date", typeof(DateTime));
        dt.Columns.Add("Amount", typeof(decimal));
        dt.Columns.Add("Description", typeof(string));
        dt.Columns.Add("DimensionSetId", typeof(int));
        dt.Columns.Add("LastModifiedDateTime", typeof(DateTime));

        foreach (var b in budgetEntries)
            dt.Rows.Add(b.SystemId, b.EntryNo, b.BudgetName, b.GLAccountNo, b.Date,
                b.Amount, b.Description, b.DimensionSetId, b.LastModifiedDateTime);

        using var conn = GetConnection();
        await conn.OpenAsync();

        await BulkInsertStagingAsync(conn, dt, "#staging_Budget");

        await new SqlCommand(@"
            MERGE analytics.fact_Budget AS target
            USING #staging_Budget AS source ON target.SystemId = source.SystemId
            WHEN MATCHED THEN UPDATE SET
                EntryNo = source.EntryNo, BudgetName = source.BudgetName,
                GLAccountNo = source.GLAccountNo, Date = source.Date,
                Amount = source.Amount, Description = source.Description,
                DimensionSetId = source.DimensionSetId,
                LastModifiedDateTime = source.LastModifiedDateTime
            WHEN NOT MATCHED THEN INSERT
                (SystemId, EntryNo, BudgetName, GLAccountNo, Date,
                 Amount, Description, DimensionSetId, LastModifiedDateTime)
            VALUES
                (source.SystemId, source.EntryNo, source.BudgetName, source.GLAccountNo,
                 source.Date, source.Amount, source.Description,
                 source.DimensionSetId, source.LastModifiedDateTime);
            DROP TABLE #staging_Budget;", conn).ExecuteNonQueryAsync();

        _logger.LogInformation("Upserted {Count} GL Budget Entries", budgetEntries.Count);
    }

    private static async Task BulkInsertStagingAsync(SqlConnection conn, DataTable dt, string stagingTable)
    {
        // Create staging table matching the DataTable structure
        var columns = string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c =>
            $"[{c.ColumnName}] {GetSqlType(c.DataType)}"));

        await new SqlCommand(
            $"CREATE TABLE {stagingTable} ({columns})", conn)
            .ExecuteNonQueryAsync();

        // Bulk insert into staging
        using var bulk = new SqlBulkCopy(conn)
        {
            DestinationTableName = stagingTable,
            BulkCopyTimeout = 120
        };

        foreach (DataColumn col in dt.Columns)
            bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        await bulk.WriteToServerAsync(dt);
    }

    private static string GetSqlType(Type type)
    {
        return type switch
        {
            _ when type == typeof(Guid) => "UNIQUEIDENTIFIER",
            _ when type == typeof(int) => "INT",
            _ when type == typeof(decimal) => "DECIMAL(18,2)",
            _ when type == typeof(bool) => "BIT",
            _ when type == typeof(DateTime) => "DATETIME2",
            _ => "NVARCHAR(MAX)"
        };
    }
}