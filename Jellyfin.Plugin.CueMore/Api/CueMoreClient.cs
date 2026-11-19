using System.Net.Http.Json;
using Jellyfin.Plugin.CueMore.Configuration;
using Jellyfin.Plugin.CueMore.Scrobbling;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CueMore.Api;

/// <summary>
/// POSTs scrobble payloads to {ApiBaseUrl}/api/jellyfin/plugin/scrobble with
/// the paired account's token in the x-cuemore-token header. One retry on
/// HttpRequestException — scrobble must never throw into the event loop or
/// Jellyfin playback pipeline. Returns whether the event got through, so
/// callers only record what CueMore actually received.
/// </summary>
public class CueMoreClient
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<CueMoreClient> _logger;

    public CueMoreClient(IHttpClientFactory factory, ILogger<CueMoreClient> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<bool> SendAsync(PluginConfiguration config, ScrobbleTarget target, ScrobblePayload payload, CancellationToken ct)
    {
        if (target == null || string.IsNullOrEmpty(target.ScrobbleToken) || string.IsNullOrEmpty(config.ApiBaseUrl))
        {
            return false;
        }

        var url = $"{config.ApiBaseUrl.TrimEnd('/')}/api/jellyfin/plugin/scrobble";
        var http = _factory.CreateClient(HttpClientNames.CueMore);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent.Value);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.TryAddWithoutValidation("x-cuemore-token", target.ScrobbleToken);

                var response = await http.SendAsync(request, ct).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Token revoked or rotated on CueMore: mark the link invalid
                    // so it stops producing dead requests until re-paired.
                    if (target.Link != null && Plugin.Instance != null)
                    {
                        target.Link.Invalid = true;
                        Plugin.Instance.SaveConfiguration();
                    }
                    _logger.LogWarning("[CueMore] Scrobble token rejected (401) — link marked invalid, re-pair required.");
                    return false;
                }

                response.EnsureSuccessStatusCode();

                if (Plugin.Instance != null)
                {
                    if (target.Link != null)
                    {
                        target.Link.LastScrobbleAt = DateTime.UtcNow;
                        target.Link.ScrobbleCount++;
                    }
                    else
                    {
                        Plugin.Instance.Configuration.LastScrobbleAt = DateTime.UtcNow;
                        Plugin.Instance.Configuration.ScrobbleCount++;
                    }
                    Plugin.Instance.SaveConfiguration();
                }
                return true;
            }
            catch (HttpRequestException ex) when (attempt == 1)
            {
                _logger.LogDebug(ex, "[CueMore] POST attempt 1 failed for event {Event}, retrying", payload.Event);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CueMore] POST failed for event {Event}", payload.Event);
                return false;
            }
        }
        return false;
    }
}

internal static class UserAgent
{
    public static readonly string PluginVersion = typeof(UserAgent).Assembly.GetName().Version?.ToString() ?? "0";

    public static readonly string Value = $"CueMore-Jellyfin/{PluginVersion}";
}
