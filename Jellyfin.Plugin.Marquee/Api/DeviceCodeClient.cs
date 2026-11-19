using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Marquee.Api;

/// <summary>
/// Talks to the Marquee device-code pairing endpoints:
///   POST /api/jellyfin/plugin/pair/start  -> issues a user_code + device_code
///   POST /api/jellyfin/plugin/pair/poll   -> exchanges a confirmed device_code for a scrobble token
/// </summary>
public class DeviceCodeClient
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<DeviceCodeClient> _logger;

    public DeviceCodeClient(IHttpClientFactory factory, ILogger<DeviceCodeClient> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<DeviceCodeResponse?> RequestCodeAsync(string apiBaseUrl, string jellyfinUserId, string jellyfinUserName, string serverLabel, CancellationToken ct)
    {
        var url = $"{apiBaseUrl.TrimEnd('/')}/api/jellyfin/plugin/pair/start";
        var http = _factory.CreateClient(HttpClientNames.Marquee);

        try
        {
            var response = await http.PostAsJsonAsync(url, new
            {
                jellyfinUserId,
                jellyfinUsername = jellyfinUserName,
                serverLabel
            }, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Marquee] Pairing code request failed");
            return null;
        }
    }

    public async Task<DeviceTokenResponse> PollTokenAsync(string apiBaseUrl, string deviceCode, CancellationToken ct)
    {
        var url = $"{apiBaseUrl.TrimEnd('/')}/api/jellyfin/plugin/pair/poll";
        var http = _factory.CreateClient(HttpClientNames.Marquee);

        try
        {
            var response = await http.PostAsJsonAsync(url, new { device_code = deviceCode }, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(cancellationToken: ct).ConfigureAwait(false)
                    ?? new DeviceTokenResponse { Error = "invalid_response" };
            }

            var err = await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(cancellationToken: ct).ConfigureAwait(false);
            return err ?? new DeviceTokenResponse { Error = "http_error" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Marquee] Pairing poll failed");
            return new DeviceTokenResponse { Error = "network_error" };
        }
    }
}

public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = string.Empty;

    [JsonPropertyName("verification_url")]
    public string VerificationUrl { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }
}

public class DeviceTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
