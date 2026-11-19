using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Marquee.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        ApiBaseUrl = "http://localhost:3000";
        UserLinks = new List<UserLink>();
        ScrobblePlaying = true;
        ScrobbleWatched = true;
    }

    /// <summary>
    /// Base URL of the Marquee instance. Default targets a local dev server;
    /// change it to your deployed Marquee URL (e.g. https://traktlite.vercel.app).
    /// </summary>
    public string ApiBaseUrl { get; set; }

    /// <summary>
    /// One entry per Jellyfin account paired with a Marquee account. Accounts
    /// with no entry here are not scrobbled at all.
    /// </summary>
    public List<UserLink> UserLinks { get; set; }

    /// <summary>Send PlaybackStart / Progress / Pause / Unpause / Stop events.</summary>
    public bool ScrobblePlaying { get; set; }

    /// <summary>Send ItemMarkedPlayed events (manual marks and auto-played flips).</summary>
    public bool ScrobbleWatched { get; set; }

    public DateTime? LastScrobbleAt { get; set; }

    public long ScrobbleCount { get; set; }

    /// <summary>Linked accounts that can scrobble (token present and still accepted).</summary>
    public IEnumerable<UserLink> ActiveLinks()
        => (UserLinks ?? new List<UserLink>()).Where(l => !string.IsNullOrEmpty(l.ScrobbleToken) && !l.Invalid);

    /// <summary>
    /// The link for a Jellyfin account, or null when that account has not been
    /// paired. Ids are compared as GUID "N" strings, case-insensitively.
    /// </summary>
    public UserLink? FindLink(string? jellyfinUserId)
    {
        if (string.IsNullOrEmpty(jellyfinUserId) || UserLinks == null) return null;
        return UserLinks.FirstOrDefault(l =>
            string.Equals(l.JellyfinUserId, jellyfinUserId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Where the events of a Jellyfin account must be sent, or null when that
    /// account is not paired and nothing should leave the server.
    /// </summary>
    public ScrobbleTarget? ResolveTarget(string? jellyfinUserId)
    {
        var link = FindLink(jellyfinUserId);
        if (link == null || string.IsNullOrEmpty(link.ScrobbleToken) || link.Invalid)
        {
            return null;
        }

        return new ScrobbleTarget { ScrobbleToken = link.ScrobbleToken, Link = link };
    }
}

/// <summary>Resolved destination for one event.</summary>
public class ScrobbleTarget
{
    public string ScrobbleToken { get; set; } = string.Empty;

    /// <summary>Link whose counters this event updates.</summary>
    public UserLink? Link { get; set; }
}

/// <summary>
/// Pairing between one Jellyfin account and one Marquee account.
/// </summary>
public class UserLink
{
    /// <summary>Jellyfin user id, GUID "N" format (32 hex chars, no dashes).</summary>
    public string JellyfinUserId { get; set; } = string.Empty;

    /// <summary>Jellyfin username at pairing time. Display only — ids are what match.</summary>
    public string JellyfinUserName { get; set; } = string.Empty;

    /// <summary>Scrobble token issued by Marquee for this account's profile.</summary>
    public string ScrobbleToken { get; set; } = string.Empty;

    /// <summary>Marquee display name, shown in the config page.</summary>
    public string MarqueeUsername { get; set; } = string.Empty;

    public DateTime? LinkedAt { get; set; }

    public DateTime? LastScrobbleAt { get; set; }

    public long ScrobbleCount { get; set; }

    /// <summary>Marquee answered 401: the token was revoked or replaced. Pair again.</summary>
    public bool Invalid { get; set; }
}
