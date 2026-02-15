namespace AnalyticsAPI.Sync.Models;

public class GLBudgetEntry
{
    public Guid SystemId { get; set; }
    public int EntryNo { get; set; }
    public string BudgetName { get; set; } = string.Empty;
    public string GLAccountNo { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public int DimensionSetId { get; set; }
    public DateTime LastModifiedDateTime { get; set; }
}