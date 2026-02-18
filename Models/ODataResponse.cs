namespace AnalyticsAPI.Sync.Models;

using System.Text.Json.Serialization;

public class ODataResponse<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = new();

    [JsonPropertyName("@odata.nextLink")]
    public string? ODataNextLink { get; set; }
}