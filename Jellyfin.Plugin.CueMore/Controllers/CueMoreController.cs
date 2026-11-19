using System.Linq;
using Jellyfin.Plugin.CueMore.Api;
using Jellyfin.Plugin.CueMore.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CueMore.Controllers;

/// <summary>
/// HTTP endpoints exposed by the plugin under /Plugins/CueMore.
/// Consumed exclusively from configPage.html in the plugin settings page.
/// All endpoints require Jellyfin admin rights.
///
/// Pairing is per Jellyfin account: the admin picks an account in the
/// dropdown and that account's owner confirms the short code on CueMore
/// while logged in, so the admin never gains access to their profile.
/// </summary>
[ApiController]
[Route("Plugins/CueMore")]
[Authorize(Policy = "RequiresElevation")]
public class CueMoreController : ControllerBase
{
    private readonly DeviceCodeClient _device;

    // In-memory, single-active-pairing state: one admin drives the config page
    // at a time and the user code is short-lived (10 min). If the page is
    // reloaded mid-flow the pending code is lost — acceptable UX.
    private static string? _pendingDeviceCode;
    private static string? _pendingJellyfinUserId;
    private static string? _pendingJellyfinUserName;

    public CueMoreController(DeviceCodeClient device)
    {
        _device = device;
    }

    /// <summary>
    /// Starts pairing for one Jellyfin account: requests a user_code from CueMore.
    /// </summary>
    [HttpPost("ConnectStart")]
    public async Task<ActionResult<DeviceCodeResponse>> ConnectStart([FromBody] ConnectStartDto? dto, CancellationToken ct)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg == null) return StatusCode(StatusCodes.Status500InternalServerError);

        var userId = NormalizeId(dto?.UserId);
        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { error = "missing_user_id" });
        }

        var serverLabel = $"{Request.Scheme}://{Request.Host}";
        var code = await _device.RequestCodeAsync(
            cfg.ApiBaseUrl, userId, dto?.UserName ?? string.Empty, serverLabel, ct);
        if (code == null) return StatusCode(StatusCodes.Status502BadGateway, new { error = "pair_start_failed" });

        _pendingDeviceCode = code.DeviceCode;
        _pendingJellyfinUserId = userId;
        _pendingJellyfinUserName = dto?.UserName ?? string.Empty;
        return code;
    }

    /// <summary>
    /// Polls CueMore for the token of the pending pairing. On success stores
    /// the link between that Jellyfin account and the CueMore account that
    /// confirmed the code.
    /// </summary>
    [HttpPost("Poll")]
    public async Task<ActionResult<PollStatus>> Poll(CancellationToken ct)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg == null) return StatusCode(StatusCodes.Status500InternalServerError);

        if (string.IsNullOrEmpty(_pendingDeviceCode) || string.IsNullOrEmpty(_pendingJellyfinUserId))
        {
            return Ok(new PollStatus { Status = "no_pending_code" });
        }

        var result = await _device.PollTokenAsync(cfg.ApiBaseUrl, _pendingDeviceCode, ct);

        if (!string.IsNullOrEmpty(result.AccessToken))
        {
            var userId = _pendingJellyfinUserId!;
            var userName = _pendingJellyfinUserName ?? string.Empty;
            var cuemoreUser = result.Username ?? string.Empty;

            cfg.UserLinks ??= new List<UserLink>();

            // A CueMore account holds a single Jellyfin scrobble token per
            // Jellyfin user. If this CueMore account was already linked to
            // another Jellyfin account on this server, that older link now
            // points at a dead token — drop it instead of leaving it broken.
            var stale = cfg.UserLinks
                .Where(l => !string.Equals(l.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(l.CueMoreUsername)
                            && string.Equals(l.CueMoreUsername, cuemoreUser, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var link in stale) cfg.UserLinks.Remove(link);

            var existing = cfg.FindLink(userId);
            if (existing == null)
            {
                existing = new UserLink { JellyfinUserId = userId };
                cfg.UserLinks.Add(existing);
            }

            existing.JellyfinUserName = userName;
            existing.ScrobbleToken = result.AccessToken!;
            existing.CueMoreUsername = cuemoreUser;
            existing.LinkedAt = DateTime.UtcNow;
            existing.LastScrobbleAt = null;
            existing.ScrobbleCount = 0;
            existing.Invalid = false;

            Plugin.Instance!.SaveConfiguration();
            ClearPending();
            return Ok(new PollStatus
            {
                Status = "connected",
                Username = cuemoreUser,
                UserId = userId,
                StaleLinksRemoved = stale.Count
            });
        }

        return Ok(new PollStatus { Status = result.Error ?? "authorization_pending" });
    }

    [HttpPost("CancelPairing")]
    public IActionResult CancelPairing()
    {
        ClearPending();
        return NoContent();
    }

    /// <summary>Unlinks a Jellyfin account: its playback stops scrobbling.</summary>
    [HttpPost("Disconnect")]
    public IActionResult Disconnect([FromBody] DisconnectDto? dto)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg == null) return StatusCode(StatusCodes.Status500InternalServerError);

        var userId = NormalizeId(dto?.UserId);
        if (string.IsNullOrEmpty(userId)) return BadRequest(new { error = "missing_user_id" });

        var link = cfg.FindLink(userId);
        if (link != null)
        {
            cfg.UserLinks?.Remove(link);
            Plugin.Instance!.SaveConfiguration();
        }
        return NoContent();
    }

    /// <summary>Current configuration snapshot for the settings page.</summary>
    [HttpGet("Status")]
    public ActionResult<StatusSnapshot> Status()
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg == null) return StatusCode(StatusCodes.Status500InternalServerError);

        return new StatusSnapshot
        {
            Links = (cfg.UserLinks ?? new List<UserLink>()).Select(l => new UserLinkDto
            {
                UserId = l.JellyfinUserId,
                UserName = l.JellyfinUserName,
                Username = l.CueMoreUsername,
                LastScrobbleAt = l.LastScrobbleAt,
                ScrobbleCount = l.ScrobbleCount,
                Invalid = l.Invalid,
                LinkedAt = l.LinkedAt
            }).ToList(),
            ApiBaseUrl = cfg.ApiBaseUrl,
            ScrobblePlaying = cfg.ScrobblePlaying,
            ScrobbleWatched = cfg.ScrobbleWatched,
            LastScrobbleAt = cfg.LastScrobbleAt,
            ScrobbleCount = cfg.ScrobbleCount
        };
    }

    /// <summary>Update plugin-wide settings (CueMore URL, event toggles).</summary>
    [HttpPost("Settings")]
    public IActionResult Settings([FromBody] SettingsUpdateDto? dto)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg == null) return StatusCode(StatusCodes.Status500InternalServerError);

        if (dto == null) return BadRequest();

        if (!string.IsNullOrWhiteSpace(dto.ApiBaseUrl))
        {
            cfg.ApiBaseUrl = dto.ApiBaseUrl.Trim().TrimEnd('/');
        }
        if (dto.ScrobblePlaying.HasValue) cfg.ScrobblePlaying = dto.ScrobblePlaying.Value;
        if (dto.ScrobbleWatched.HasValue) cfg.ScrobbleWatched = dto.ScrobbleWatched.Value;

        Plugin.Instance!.SaveConfiguration();
        return NoContent();
    }

    private static void ClearPending()
    {
        _pendingDeviceCode = null;
        _pendingJellyfinUserId = null;
        _pendingJellyfinUserName = null;
    }

    /// <summary>Jellyfin ids travel as dashed GUIDs in the web client; store them as "N".</summary>
    private static string NormalizeId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return Guid.TryParse(raw, out var id) ? id.ToString("N") : string.Empty;
    }
}

public class ConnectStartDto
{
    public string? UserId { get; set; }
    public string? UserName { get; set; }
}

public class DisconnectDto
{
    public string? UserId { get; set; }
}

public class PollStatus
{
    public string Status { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? UserId { get; set; }
    public int StaleLinksRemoved { get; set; }
}

public class UserLinkDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime? LinkedAt { get; set; }
    public DateTime? LastScrobbleAt { get; set; }
    public long ScrobbleCount { get; set; }
    public bool Invalid { get; set; }
}

public class StatusSnapshot
{
    public List<UserLinkDto> Links { get; set; } = new();
    public string ApiBaseUrl { get; set; } = string.Empty;
    public bool ScrobblePlaying { get; set; }
    public bool ScrobbleWatched { get; set; }
    public DateTime? LastScrobbleAt { get; set; }
    public long ScrobbleCount { get; set; }
}

public class SettingsUpdateDto
{
    public string? ApiBaseUrl { get; set; }
    public bool? ScrobblePlaying { get; set; }
    public bool? ScrobbleWatched { get; set; }
}
