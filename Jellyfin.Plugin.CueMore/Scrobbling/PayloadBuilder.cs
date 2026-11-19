using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.CueMore.Scrobbling;

/// <summary>
/// Maps Jellyfin SessionInfo + item data into the CueMore scrobble payload.
/// Only Movie and Episode kinds are supported — anything else is filtered
/// out upstream and never reaches the builder.
/// </summary>
public class PayloadBuilder
{
    public ScrobblePayload Build(string eventName, BaseItem item, SessionInfo session, long positionTicks, bool isPaused, bool played)
    {
        var payload = new ScrobblePayload
        {
            Event = eventName,
            UserId = session.UserId.ToString("N"),
            UserName = session.UserName ?? string.Empty,
            ItemId = item.Id.ToString("N"),
            ItemType = item is Episode ? "Episode" : "Movie",
            Name = item.Name ?? string.Empty,
            ProviderIds = BuildProviderIds(item.ProviderIds),
            PositionTicks = positionTicks,
            RuntimeTicks = item.RunTimeTicks ?? 0,
            IsPaused = isPaused,
            Played = played,
            Timestamp = DateTime.UtcNow.ToString("O")
        };

        AttachEpisodeMetadata(payload, item);
        return payload;
    }

    /// <summary>
    /// Build a payload for an out-of-band UserDataSaved event (mark watched).
    /// No session involved. Note: we intentionally do not reference Jellyfin's
    /// User entity type here — it moved namespaces between releases. A plain
    /// Guid + optional username string is ABI-stable.
    /// </summary>
    public ScrobblePayload BuildUserData(string eventName, BaseItem item, UserItemData userData, Guid userId, string? userName, string saveReason, bool played)
    {
        var payload = BuildStandalone(eventName, item, userId, userName, userData.PlaybackPositionTicks, isPaused: false, played);
        payload.Played = played;
        payload.Timestamp = DateTime.UtcNow.ToString("O");
        return payload;
    }

    /// <summary>Build a payload without a SessionInfo (playback stop path).</summary>
    public ScrobblePayload BuildStandalone(string eventName, BaseItem item, Guid userId, string? userName, long positionTicks, bool isPaused, bool played)
    {
        var payload = new ScrobblePayload
        {
            Event = eventName,
            UserId = userId.ToString("N"),
            UserName = userName ?? string.Empty,
            ItemId = item.Id.ToString("N"),
            ItemType = item is Episode ? "Episode" : "Movie",
            Name = item.Name ?? string.Empty,
            ProviderIds = BuildProviderIds(item.ProviderIds),
            PositionTicks = positionTicks,
            RuntimeTicks = item.RunTimeTicks ?? 0,
            IsPaused = isPaused,
            Played = played,
            Timestamp = DateTime.UtcNow.ToString("O")
        };

        AttachEpisodeMetadata(payload, item);
        return payload;
    }

    private static void AttachEpisodeMetadata(ScrobblePayload payload, BaseItem item)
    {
        if (item is Episode episode)
        {
            payload.SeriesName = episode.SeriesName ?? string.Empty;
            payload.Season = episode.ParentIndexNumber ?? 0;
            payload.Episode = episode.IndexNumber ?? 0;

            var series = episode.Series;
            if (series != null)
            {
                payload.SeriesProviderIds = BuildProviderIds(series.ProviderIds);
                if (series.ProductionYear.HasValue && series.ProductionYear.Value > 0)
                {
                    payload.ProductionYear = series.ProductionYear.Value;
                }
            }
        }
        else if (item.ProductionYear.HasValue && item.ProductionYear.Value > 0)
        {
            payload.ProductionYear = item.ProductionYear.Value;
        }
    }

    private static ProviderIdsPayload BuildProviderIds(IReadOnlyDictionary<string, string>? ids)
    {
        var result = new ProviderIdsPayload();
        if (ids == null) return result;

        if (ids.TryGetValue(MetadataProvider.Imdb.ToString(), out var imdb) && !string.IsNullOrEmpty(imdb))
            result.Imdb = imdb;
        if (ids.TryGetValue(MetadataProvider.Tmdb.ToString(), out var tmdb) && !string.IsNullOrEmpty(tmdb))
            result.Tmdb = tmdb;
        if (ids.TryGetValue(MetadataProvider.Tvdb.ToString(), out var tvdb) && !string.IsNullOrEmpty(tvdb))
            result.Tvdb = tvdb;

        return result;
    }
}
