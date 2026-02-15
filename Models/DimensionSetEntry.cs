namespace AnalyticsAPI.Sync.Models;

public class DimensionSetEntry
{
    public Guid SystemId { get; set; }
    public int DimensionSetId { get; set; }
    public string DimensionCode { get; set; } = string.Empty;
    public string DimensionValueCode { get; set; } = string.Empty;
    public string DimensionValueName { get; set; } = string.Empty;
    public DateTime LastModifiedDateTime { get; set; }
}