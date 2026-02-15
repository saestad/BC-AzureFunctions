namespace AnalyticsAPI.Sync.Services;

using System.Net.Http.Headers;
using AnalyticsAPI.Sync.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

public class BCApiService
{
    private readonly HttpClient _httpClient;
    private readonly TokenService _tokenService;
    private readonly ILogger<BCApiService> _logger;
    private readonly string _baseUrl;

    public BCApiService(HttpClient httpClient, TokenService tokenService, ILogger<BCApiService> logger)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _logger = logger;

        var tenantId = Environment.GetEnvironmentVariable("BC_TENANT_ID");
        var environment = Environment.GetEnvironmentVariable("BC_ENVIRONMENT");
        var companyId = Environment.GetEnvironmentVariable("BC_COMPANY_ID");

        _baseUrl = $"https://api.businesscentral.dynamics.com/v2.0/{tenantId}/{environment}/api/sestad/analytics/v1.0/companies({companyId})";
    }

    public async Task<List<T>> GetAllAsync<T>(string endpoint, DateTime? lastSync = null)
    {
        var token = await _tokenService.GetTokenAsync();
        var results = new List<T>();

        var url = $"{_baseUrl}/{endpoint}";

        if (lastSync.HasValue && lastSync.Value > new DateTime(2000, 1, 1))
        {
            var filterDate = lastSync.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            url += $"?$filter=lastModifiedDateTime gt {filterDate}";
        }

        while (!string.IsNullOrEmpty(url))
        {
            _logger.LogInformation("Fetching: {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("BC API error: {Content}", content);
                throw new Exception($"BC API request failed: {content}");
            }

            var odataResponse = JsonConvert.DeserializeObject<ODataResponse<T>>(content)
                ?? throw new Exception("Failed to deserialize BC API response");

            results.AddRange(odataResponse.Value);

            url = odataResponse.NextLink ?? string.Empty;

            _logger.LogInformation("Fetched {Count} records, total so far: {Total}", 
                odataResponse.Value.Count, results.Count);
        }

        return results;
    }
}