namespace AnalyticsAPI.Sync.Models;

public class ODataResponse<T>
{
    public List<T> Value { get; set; } = new();

    [Newtonsoft.Json.JsonProperty("@odata.nextLink")]
    public string? NextLink { get; set; }
}