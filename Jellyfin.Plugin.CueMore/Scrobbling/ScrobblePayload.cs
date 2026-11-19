using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.CueMore.Scrobbling;

/// <summary>
/// JSON shape POSTed to /api/jellyfin/plugin/scrobble on the CueMore server.
/// </summary>
public class ScrobblePayload
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = string.Empty;

    [JsonPropertyName("item_type")]
    public string ItemType { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("provider_ids")]
    public ProviderIdsPayload ProviderIds { get; set; } = new();

    [JsonPropertyName("series_name")]
    public string SeriesName { get; set; } = string.Empty;

    [JsonPropertyName("series_provider_ids")]
    public ProviderIdsPayload SeriesProviderIds { get; set; } = new();

    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("episode")]
    public int Episode { get; set; }

    [JsonPropertyName("production_year")]
    public int ProductionYear { get; set; }

    [JsonPropertyName("position_ticks")]
    public long PositionTicks { get; set; }

    [JsonPropertyName("runtime_ticks")]
    public long RuntimeTicks { get; set; }

    [JsonPropertyName("is_paused")]
    public bool IsPaused { get; set; }

    [JsonPropertyName("played")]
    public bool Played { get; set; }

    /// <summary>UTC time the event happened, ISO-8601.</summary>
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;
}

public class ProviderIdsPayload
{
    [JsonPropertyName("imdb")]
    public string Imdb { get; set; } = string.Empty;

    [JsonPropertyName("tmdb")]
    public string Tmdb { get; set; } = string.Empty;

    [JsonPropertyName("tvdb")]
    public string Tvdb { get; set; } = string.Empty;
}
