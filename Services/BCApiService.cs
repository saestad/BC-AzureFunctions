namespace AnalyticsAPI.Sync.Services;

using AnalyticsAPI.Sync.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

public class BCApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BCApiService> _logger;

    public BCApiService(ILogger<BCApiService> logger)
    {
        _httpClient = new HttpClient();
        _logger = logger;
    }

    public async Task<List<T>> GetAllAsync<T>(
        string token, 
        Guid bcTenantId, 
        string environmentName, 
        Guid companyId, 
        string endpoint, 
        DateTime? lastSync = null)
    {
        var allRecords = new List<T>();
        
        var baseUrl = $"https://api.businesscentral.dynamics.com/v2.0/{bcTenantId}/{environmentName}/api/sestad/analytics/v1.0/companies({companyId})/{endpoint}";
        
        var url = lastSync.HasValue 
            ? $"{baseUrl}?$filter=lastModifiedDateTime gt {lastSync.Value:yyyy-MM-ddTHH:mm:ssZ}"
            : baseUrl;

        while (!string.IsNullOrEmpty(url))
        {
            _logger.LogInformation("Fetching: {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("BC API error: {Content}", content);
                throw new Exception($"BC API request failed: {content}");
            }

            var odataResponse = JsonSerializer.Deserialize<ODataResponse<T>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (odataResponse?.Value != null)
            {
                allRecords.AddRange(odataResponse.Value);
                _logger.LogInformation("Fetched {Count} records, total so far: {Total}", odataResponse.Value.Count, allRecords.Count);
            }

            url = odataResponse?.ODataNextLink ?? string.Empty;
        }

        return allRecords;
    }
}