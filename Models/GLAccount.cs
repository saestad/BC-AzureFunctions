namespace AnalyticsAPI.Sync.Models;

public class GLAccount
{
    public Guid SystemId { get; set; }
    public string No { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string AccountCategory { get; set; } = string.Empty;
    public string AccountSubcategory { get; set; } = string.Empty;
    public int AccountSubcategoryEntryNo { get; set; }
    public string IncomeBalance { get; set; } = string.Empty;
    public int Indentation { get; set; }
    public bool Blocked { get; set; }
    public DateTime LastModifiedDateTime { get; set; }
}