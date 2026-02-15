namespace AnalyticsAPI.Sync.Services;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

public class TokenService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TokenService> _logger;
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly string _clientSecret;

    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public TokenService(HttpClient httpClient, ILogger<TokenService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _tenantId = Environment.GetEnvironmentVariable("BC_TENANT_ID") ?? throw new Exception("BC_TENANT_ID not configured");
        _clientId = Environment.GetEnvironmentVariable("BC_CLIENT_ID") ?? throw new Exception("BC_CLIENT_ID not configured");
        _clientSecret = Environment.GetEnvironmentVariable("BC_CLIENT_SECRET") ?? throw new Exception("BC_CLIENT_SECRET not configured");
    }

    public async Task<string> GetTokenAsync()
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
        {
            return _cachedToken;
        }

        _logger.LogInformation("Fetching new OAuth token from Azure AD");

        var url = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";

        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _clientId),
            new KeyValuePair<string, string>("client_secret", _clientSecret),
            new KeyValuePair<string, string>("scope", "https://api.businesscentral.dynamics.com/.default")
        });

        var response = await _httpClient.PostAsync(url, body);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to get token: {Content}", content);
            throw new Exception($"Token request failed: {content}");
        }

        var json = JsonDocument.Parse(content);
        _cachedToken = json.RootElement.GetProperty("access_token").GetString()
            ?? throw new Exception("access_token missing from response");

        var expiresIn = json.RootElement.GetProperty("expires_in").GetInt32();
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

        _logger.LogInformation("Token acquired successfully, expires in {ExpiresIn}s", expiresIn);

        return _cachedToken;
    }
}