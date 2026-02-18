namespace AnalyticsAPI.Sync.Services;

using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

public class TokenService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TokenService> _logger;
    private readonly Dictionary<Guid, CachedToken> _tokenCache = new();
    private readonly string _clientSecret;

    public TokenService(ILogger<TokenService> logger)
    {
        _httpClient = new HttpClient();
        _logger = logger;
        _clientSecret = Environment.GetEnvironmentVariable("BC_CLIENT_SECRET")
            ?? throw new Exception("BC_CLIENT_SECRET not configured");
    }

    public async Task<string> GetTokenAsync(Guid tenantId, Guid clientId)
    {
        // Check cache
        if (_tokenCache.TryGetValue(clientId, out var cached) && cached.ExpiresAt > DateTime.UtcNow.AddSeconds(60))
        {
            _logger.LogInformation("Using cached token for client {ClientId}", clientId);
            return cached.AccessToken;
        }

        // Fetch new token
        _logger.LogInformation("Fetching new OAuth token from Azure AD");

        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId.ToString()),
            new KeyValuePair<string, string>("client_secret", _clientSecret),
            new KeyValuePair<string, string>("scope", "https://api.businesscentral.dynamics.com/.default"),
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        var response = await _httpClient.PostAsync(tokenUrl, body);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to get token: {Content}", content);
            throw new Exception($"Token request failed: {content}");
        }

        var tokenResponse = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(content, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (tokenResponse?.AccessToken == null)
        {
            _logger.LogError("Token response missing access_token. Response: {Content}", content);
            throw new Exception("Token response missing access_token");
        }

        // Cache token
        _tokenCache[clientId] = new CachedToken
        {
            AccessToken = tokenResponse.AccessToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        };

        _logger.LogInformation("Token acquired successfully, expires in {Seconds}s", tokenResponse.ExpiresIn);
        return tokenResponse.AccessToken;
    }

    private class CachedToken
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
        
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}