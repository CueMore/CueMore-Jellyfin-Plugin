# CueMore for Jellyfin

A Jellyfin plugin that scrobbles your watch activity to [CueMore](https://cuemore.vercel.app/) — your movies and episodes are marked as watched automatically when you finish them, and now-playing status shows up on your CueMore integrations page.

## Features

- **Watched scrobbling** — finishing a movie or episode marks it as watched in CueMore, including season/episode numbers for TV shows.
- **Manual marks** — marking an item as played/unplayed in Jellyfin syncs to CueMore too.
- **Now playing** — shows your current playback on the CueMore integrations page in real time.
- **Account pairing** — link each Jellyfin user to their own CueMore account with a short pairing code. No API keys to paste.
- **Works behind NAT** — the plugin only makes outbound requests to CueMore, so no port forwarding or webhook configuration is needed.

## Requirements

- Jellyfin **10.10** or newer (10.10, 10.11, and 12.x supported)
- A free [CueMore](https://cuemore.vercel.app/) account

## Installation

Open Jellyfin → **Dashboard → Plugins → Repositories → Manage Repositories → Add Repository**.

Name: `CueMore`. URL — pick the one matching your server:

| Jellyfin server | Repository URL |
|---|---|
| 12.x and 10.11.x | `https://raw.githubusercontent.com/CueMore/CueMore-Jellyfin-Plugin/main/manifest.json` |
| 10.10.x (LTS) | `https://raw.githubusercontent.com/CueMore/CueMore-Jellyfin-Plugin/main/lts/manifest.json` |

Go to **Catalog**, find **CueMore**, install. Restart Jellyfin when prompted.

**Manual install:** alternatively, download the matching zip from [Releases](https://github.com/CueMore/CueMore-Jellyfin-Plugin/releases), unzip it into your Jellyfin `plugins/` folder, and restart the server.

## Setup

1. Open **Dashboard → Plugins → CueMore**.
2. Pick a Jellyfin account and click **Connect to CueMore**.
3. A short code is shown — open the verification link, make sure you're logged into CueMore, and confirm the code.
4. The plugin page flips to connected. Done — finished movies and episodes are marked as watched in CueMore automatically.

## How it works

The plugin listens to Jellyfin's playback events (`PlaybackStarted`, `PlaybackProgress`, `PlaybackStopped`, `UserDataSaved`) and posts scrobble events to CueMore's ingest API, authenticated by a per-account token issued during pairing. Playback progress events are throttled to one request every 30 seconds per session.

Scrobbling only records *completed* watches — stopping a movie partway through (or rewatching something already played) does not create a watch entry unless you actually mark it as played.

## Privacy

The plugin sends only what's needed to scrobble: item IDs, titles, media type, provider IDs (TMDB/IMDB), season/episode numbers, and playback position/duration. No watch history is uploaded in bulk — only events as they happen.

## Credits

Built with heavy inspiration from [Wetrakr's Jellyfin plugin](https://github.com/wetrakr/wetrakr-jf) — thank you to the Wetrakr team for the open-source reference on Jellyfin plugin architecture and event handling.

## License

MIT
