namespace AnalyticsAPI.Sync.Services;

using AnalyticsAPI.Sync.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

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
        using var conn = GetConnection();
        await conn.OpenAsync();

        foreach (var account in accounts)
        {
            var cmd = new SqlCommand(@"
                MERGE analytics.dim_Account AS target
                USING (SELECT @SystemId AS SystemId) AS source
                ON target.SystemId = source.SystemId
                WHEN MATCHED THEN UPDATE SET
                    No = @No,
                    Name = @Name,
                    AccountType = @AccountType,
                    AccountCategory = @AccountCategory,
                    AccountSubcategory = @AccountSubcategory,
                    AccountSubcategoryEntryNo = @AccountSubcategoryEntryNo,
                    IncomeBalance = @IncomeBalance,
                    Indentation = @Indentation,
                    Blocked = @Blocked,
                    LastModifiedDateTime = @LastModifiedDateTime
                WHEN NOT MATCHED THEN INSERT
                    (SystemId, No, Name, AccountType, AccountCategory, AccountSubcategory,
                     AccountSubcategoryEntryNo, IncomeBalance, Indentation, Blocked, LastModifiedDateTime)
                VALUES
                    (@SystemId, @No, @Name, @AccountType, @AccountCategory, @AccountSubcategory,
                     @AccountSubcategoryEntryNo, @IncomeBalance, @Indentation, @Blocked, @LastModifiedDateTime);",
                conn);

            cmd.Parameters.AddWithValue("@SystemId", account.SystemId);
            cmd.Parameters.AddWithValue("@No", account.No);
            cmd.Parameters.AddWithValue("@Name", account.Name);
            cmd.Parameters.AddWithValue("@AccountType", account.AccountType);
            cmd.Parameters.AddWithValue("@AccountCategory", account.AccountCategory);
            cmd.Parameters.AddWithValue("@AccountSubcategory", account.AccountSubcategory);
            cmd.Parameters.AddWithValue("@AccountSubcategoryEntryNo", account.AccountSubcategoryEntryNo);
            cmd.Parameters.AddWithValue("@IncomeBalance", account.IncomeBalance);
            cmd.Parameters.AddWithValue("@Indentation", account.Indentation);
            cmd.Parameters.AddWithValue("@Blocked", account.Blocked);
            cmd.Parameters.AddWithValue("@LastModifiedDateTime", account.LastModifiedDateTime);

            await cmd.ExecuteNonQueryAsync();
        }

        _logger.LogInformation("Upserted {Count} GL Accounts", accounts.Count);
    }

    public async Task UpsertGLEntriesAsync(List<GLEntry> entries)
    {
        using var conn = GetConnection();
        await conn.OpenAsync();

        foreach (var entry in entries)
        {
            var cmd = new SqlCommand(@"
                MERGE analytics.fact_GL AS target
                USING (SELECT @SystemId AS SystemId) AS source
                ON target.SystemId = source.SystemId
                WHEN MATCHED THEN UPDATE SET
                    EntryNo = @EntryNo,
                    GLAccountNo = @GLAccountNo,
                    PostingDate = @PostingDate,
                    DocumentType = @DocumentType,
                    DocumentNo = @DocumentNo,
                    Description = @Description,
                    Amount = @Amount,
                    DebitAmount = @DebitAmount,
                    CreditAmount = @CreditAmount,
                    DimensionSetId = @DimensionSetId,
                    LastModifiedDateTime = @LastModifiedDateTime
                WHEN NOT MATCHED THEN INSERT
                    (SystemId, EntryNo, GLAccountNo, PostingDate, DocumentType, DocumentNo,
                     Description, Amount, DebitAmount, CreditAmount, DimensionSetId, LastModifiedDateTime)
                VALUES
                    (@SystemId, @EntryNo, @GLAccountNo, @PostingDate, @DocumentType, @DocumentNo,
                     @Description, @Amount, @DebitAmount, @CreditAmount, @DimensionSetId, @LastModifiedDateTime);",
                conn);

            cmd.Parameters.AddWithValue("@SystemId", entry.SystemId);
            cmd.Parameters.AddWithValue("@EntryNo", entry.EntryNo);
            cmd.Parameters.AddWithValue("@GLAccountNo", entry.GLAccountNo);
            cmd.Parameters.AddWithValue("@PostingDate", entry.PostingDate);
            cmd.Parameters.AddWithValue("@DocumentType", entry.DocumentType);
            cmd.Parameters.AddWithValue("@DocumentNo", entry.DocumentNo);
            cmd.Parameters.AddWithValue("@Description", entry.Description);
            cmd.Parameters.AddWithValue("@Amount", entry.Amount);
            cmd.Parameters.AddWithValue("@DebitAmount", entry.DebitAmount);
            cmd.Parameters.AddWithValue("@CreditAmount", entry.CreditAmount);
            cmd.Parameters.AddWithValue("@DimensionSetId", entry.DimensionSetId);
            cmd.Parameters.AddWithValue("@LastModifiedDateTime", entry.LastModifiedDateTime);

            await cmd.ExecuteNonQueryAsync();
        }

        _logger.LogInformation("Upserted {Count} GL Entries", entries.Count);
    }

    public async Task UpsertDimensionSetEntriesAsync(List<DimensionSetEntry> dimensions)
    {
        using var conn = GetConnection();
        await conn.OpenAsync();

        foreach (var dim in dimensions)
        {
            var cmd = new SqlCommand(@"
                MERGE analytics.dim_Dimension AS target
                USING (SELECT @SystemId AS SystemId) AS source
                ON target.SystemId = source.SystemId
                WHEN MATCHED THEN UPDATE SET
                    DimensionSetId = @DimensionSetId,
                    DimensionCode = @DimensionCode,
                    DimensionValueCode = @DimensionValueCode,
                    DimensionValueName = @DimensionValueName,
                    LastModifiedDateTime = @LastModifiedDateTime
                WHEN NOT MATCHED THEN INSERT
                    (SystemId, DimensionSetId, DimensionCode, DimensionValueCode, 
                     DimensionValueName, LastModifiedDateTime)
                VALUES
                    (@SystemId, @DimensionSetId, @DimensionCode, @DimensionValueCode,
                     @DimensionValueName, @LastModifiedDateTime);",
                conn);

            cmd.Parameters.AddWithValue("@SystemId", dim.SystemId);
            cmd.Parameters.AddWithValue("@DimensionSetId", dim.DimensionSetId);
            cmd.Parameters.AddWithValue("@DimensionCode", dim.DimensionCode);
            cmd.Parameters.AddWithValue("@DimensionValueCode", dim.DimensionValueCode);
            cmd.Parameters.AddWithValue("@DimensionValueName", dim.DimensionValueName);
            cmd.Parameters.AddWithValue("@LastModifiedDateTime", dim.LastModifiedDateTime);

            await cmd.ExecuteNonQueryAsync();
        }

        _logger.LogInformation("Upserted {Count} Dimension Set Entries", dimensions.Count);
    }

    public async Task UpsertGLBudgetEntriesAsync(List<GLBudgetEntry> budgetEntries)
    {
        using var conn = GetConnection();
        await conn.OpenAsync();

        foreach (var entry in budgetEntries)
        {
            var cmd = new SqlCommand(@"
                MERGE analytics.fact_Budget AS target
                USING (SELECT @SystemId AS SystemId) AS source
                ON target.SystemId = source.SystemId
                WHEN MATCHED THEN UPDATE SET
                    EntryNo = @EntryNo,
                    BudgetName = @BudgetName,
                    GLAccountNo = @GLAccountNo,
                    Date = @Date,
                    Amount = @Amount,
                    Description = @Description,
                    DimensionSetId = @DimensionSetId,
                    LastModifiedDateTime = @LastModifiedDateTime
                WHEN NOT MATCHED THEN INSERT
                    (SystemId, EntryNo, BudgetName, GLAccountNo, Date,
                     Amount, Description, DimensionSetId, LastModifiedDateTime)
                VALUES
                    (@SystemId, @EntryNo, @BudgetName, @GLAccountNo, @Date,
                     @Amount, @Description, @DimensionSetId, @LastModifiedDateTime);",
                conn);

            cmd.Parameters.AddWithValue("@SystemId", entry.SystemId);
            cmd.Parameters.AddWithValue("@EntryNo", entry.EntryNo);
            cmd.Parameters.AddWithValue("@BudgetName", entry.BudgetName);
            cmd.Parameters.AddWithValue("@GLAccountNo", entry.GLAccountNo);
            cmd.Parameters.AddWithValue("@Date", entry.Date);
            cmd.Parameters.AddWithValue("@Amount", entry.Amount);
            cmd.Parameters.AddWithValue("@Description", entry.Description);
            cmd.Parameters.AddWithValue("@DimensionSetId", entry.DimensionSetId);
            cmd.Parameters.AddWithValue("@LastModifiedDateTime", entry.LastModifiedDateTime);

            await cmd.ExecuteNonQueryAsync();
        }

        _logger.LogInformation("Upserted {Count} GL Budget Entries", budgetEntries.Count);
    }
}