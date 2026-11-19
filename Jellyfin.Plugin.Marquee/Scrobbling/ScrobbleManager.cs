using Jellyfin.Plugin.Marquee.Configuration;
using Jellyfin.Plugin.Marquee.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Marquee.Scrobbling;

/// <summary>
/// Subscribes to Jellyfin session + user-data events and forwards them to
/// Marquee. Runs as an IHostedService — Jellyfin lifecycle-manages it.
///
/// Event translation:
///   PlaybackStart    -> "PlaybackStart"   (live now-playing + position seed)
///   PlaybackProgress -> "PlaybackProgress" (throttled to one per 30s per
///                       session — keeps Marquee's live card fresh)
///   PlaybackStopped  -> "PlaybackStop"    (always sent so Marquee clears the
///                       live session; played=true only when the position
///                       reached ~90% of the runtime — Jellyfin's own
///                       PlayedToCompletion flag is also true for stops on
///                       already-played rewatch items)
///   UserDataSaved TogglePlayed flip       -> "ItemMarkedPlayed"
///   UserDataSaved PlaybackProgress flip   -> "ItemMarkedPlayed"
///     (Jellyfin marks items played mid-playback at MaxResumePct; forwarding
///     the observed flip means the watch is recorded even when the client
///     never reports the item as stopped — e.g. autoplay into the next episode)
///
/// Only Movie and Episode items are dispatched, and only for Jellyfin
/// accounts paired with a Marquee account. Failures are logged and
/// swallowed — scrobble must never break playback.
/// </summary>
public class ScrobbleManager : IHostedService
{
    private readonly ISessionManager _sessions;
    private readonly IUserDataManager _userData;
    private readonly MarqueeClient _client;
    private readonly PayloadBuilder _builder;
    private readonly PlayedStateTracker _playedState;
    private readonly ProgressPlayedTracker _progressPlayed;
    private readonly ProgressThrottle _progress;
    private readonly ILogger<ScrobbleManager> _logger;

    public ScrobbleManager(
        ISessionManager sessions,
        IUserDataManager userData,
        MarqueeClient client,
        PayloadBuilder builder,
        PlayedStateTracker playedState,
        ProgressPlayedTracker progressPlayed,
        ProgressThrottle progress,
        ILogger<ScrobbleManager> logger)
    {
        _sessions = sessions;
        _userData = userData;
        _client = client;
        _builder = builder;
        _playedState = playedState;
        _progressPlayed = progressPlayed;
        _progress = progress;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart    += OnPlaybackStart;
        _sessions.PlaybackProgress += OnPlaybackProgress;
        _sessions.PlaybackStopped  += OnPlaybackStopped;
        _userData.UserDataSaved    += OnUserDataSaved;
        _logger.LogInformation("[Marquee] ScrobbleManager started: subscribed to playback + user-data events.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart    -= OnPlaybackStart;
        _sessions.PlaybackProgress -= OnPlaybackProgress;
        _sessions.PlaybackStopped  -= OnPlaybackStopped;
        _userData.UserDataSaved    -= OnUserDataSaved;
        _logger.LogInformation("[Marquee] ScrobbleManager stopped.");
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            // Seed the throttle so the first progress tick right after start
            // (which carries the same position PlaybackStart already sent)
            // is suppressed.
            var key = SessionKey(e);
            _progress.Seed(key, DateTime.UtcNow);

            if (e.Item == null || e.Session == null) return;
            _ = DispatchAsync(e.Item, e.Session, "PlaybackStart", e.PlaybackPositionTicks ?? 0, e.IsPaused, played: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Marquee] OnPlaybackStart threw");
        }
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            if (e.Item == null || e.Session == null) return;
            var key = SessionKey(e);
            if (!_progress.ShouldDispatch(key, DateTime.UtcNow)) return;

            _ = DispatchAsync(e.Item, e.Session, "PlaybackProgress", e.PlaybackPositionTicks ?? 0, e.IsPaused, played: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Marquee] OnPlaybackProgress threw");
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        try
        {
            var key = SessionKey(e);
            _progress.Remove(key);
            if (e.Item == null || e.Session == null) return;

            var positionTicks = e.PlaybackPositionTicks ?? 0;
            var runtimeTicks = e.Item.RunTimeTicks ?? 0;
            var ratio = runtimeTicks > 0 ? (double)positionTicks / runtimeTicks : 0;

            // Jellyfin's PlayedToCompletion is also true when stopping an
            // item that was ALREADY marked played (rewatches), so a 2-minute
            // dip into a rewatch would scrobble. Require real progress
            // through the file — same ~90% bar Jellyfin uses for its own
            // auto played-mark. The stop event is sent either way so Marquee
            // clears the live session.
            var played = ratio >= 0.9 || (e.PlayedToCompletion && runtimeTicks <= 0);

            _ = DispatchAsync(e.Item, e.Session, "PlaybackStop", positionTicks, isPaused: false, played);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Marquee] OnPlaybackStopped threw");
        }
    }

    // --- UserData events (mark watched, auto-played flips) ---

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Everything wrapped in try/catch — this handler runs on the HTTP
        // request thread of Jellyfin's own FavoriteItems/PlayedItems
        // endpoint; if it throws, the original request 500s for the user.
        try
        {
            if (e.SaveReason == UserDataSaveReason.PlaybackProgress)
            {
                OnPlaybackProgressSaved(e);
                return;
            }

            if (e.SaveReason != UserDataSaveReason.TogglePlayed)
            {
                return;
            }

            if (!ShouldDispatch(e.Item)) return;

            // Jellyfin raises TogglePlayed even when the value is unchanged,
            // so re-marks from other plugins would flood the API. Only an
            // actual flip of the played flag carries new information.
            var key = PlayedStateTracker.KeyFor(e.UserId, e.Item.Id);
            if (!_playedState.ShouldDispatch(key, e.UserData.Played, DateTime.UtcNow))
            {
                return;
            }

            // Un-marks carry no watch history for Marquee — only send plays.
            if (!e.UserData.Played) return;

            _ = DispatchUserDataAsync(e, "ItemMarkedPlayed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Marquee] OnUserDataSaved threw");
        }
    }

    /// <summary>
    /// Forwards the played mark Jellyfin sets on its own once playback passes
    /// MaxResumePct, so the item is watched on Marquee even if the client
    /// never reports the stop. Runs on every progress tick: keep it cheap.
    /// </summary>
    private void OnPlaybackProgressSaved(UserDataSaveEventArgs e)
    {
        if (!ShouldDispatch(e.Item)) return;

        // Without a known runtime (or under MinResumeDuration) Jellyfin marks
        // the item played on the very first tick; that is not a finished watch.
        if ((e.Item.RunTimeTicks ?? 0) < MinRuntimeForProgressPlayed.Ticks) return;

        var key = PlayedStateTracker.KeyFor(e.UserId, e.Item.Id);
        if (!_progressPlayed.ObserveFlipToPlayed(key, e.UserData.Played, DateTime.UtcNow)) return;

        // Record it in the toggle gate as well, so a later redundant "mark as
        // played" does not send it a second time.
        _playedState.ShouldDispatch(key, true, DateTime.UtcNow);

        _ = DispatchUserDataAsync(e, "ItemMarkedPlayed");
    }

    private async Task DispatchUserDataAsync(UserDataSaveEventArgs e, string eventName)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;
            if (eventName == "ItemMarkedPlayed" && !config.ScrobbleWatched) return;

            var target = config.ResolveTarget(e.UserId.ToString("N"));
            if (target == null) return;

            var userName = TryGetUserName(e);
            var payload = _builder.BuildUserData(eventName, e.Item, e.UserData, e.UserId, userName, e.SaveReason.ToString(), e.UserData.Played);
            await _client.SendAsync(config, target, payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Marquee] UserDataSaved dispatch failed for event {Event}", eventName);
        }
    }

    private Task DispatchAsync(BaseItem item, SessionInfo session, string eventName, long positionTicks, bool isPaused, bool played)
        => DispatchAsync(item, session.UserId, session.UserName, eventName, positionTicks, isPaused, played);

    private async Task DispatchAsync(BaseItem item, Guid userId, string? userName, string eventName, long positionTicks, bool isPaused, bool played)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;
            if (!config.ScrobblePlaying && eventName != "ItemMarkedPlayed") return;

            var target = config.ResolveTarget(userId.ToString("N"));
            if (target == null) return;

            var payload = _builder.BuildStandalone(eventName, item, userId, userName, positionTicks, isPaused, played);
            await _client.SendAsync(config, target, payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Marquee] Dispatch failed for event {Event}", eventName);
        }
    }

    private static string? TryGetUserName(UserDataSaveEventArgs e)
    {
        try
        {
            var userProp = e.GetType().GetProperty("User");
            var user = userProp?.GetValue(e);
            if (user == null) return null;

            var nameProp = user.GetType().GetProperty("Username") ?? user.GetType().GetProperty("Name");
            return nameProp?.GetValue(user) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Jellyfin's default MinResumeDurationSeconds.</summary>
    private static readonly TimeSpan MinRuntimeForProgressPlayed = TimeSpan.FromMinutes(5);

    private bool ShouldDispatch(BaseItem? item)
    {
        if (item == null) return false;
        return item is Movie || item is Episode;
    }

    private static string SessionKey(PlaybackProgressEventArgs e)
        => e.PlaySessionId ?? e.Session?.Id ?? string.Empty;

    private static string SessionKey(PlaybackStopEventArgs e)
        => e.PlaySessionId ?? e.Session?.Id ?? string.Empty;
}
