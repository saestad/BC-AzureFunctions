namespace AnalyticsAPI.Sync.Models;

public class GLEntry
{
    public Guid SystemId { get; set; }
    public int EntryNo { get; set; }
    public string GLAccountNo { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNo { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public int DimensionSetId { get; set; }
    public DateTime LastModifiedDateTime { get; set; }
}