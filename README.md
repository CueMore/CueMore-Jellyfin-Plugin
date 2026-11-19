# Marquee Jellyfin Plugin

Jellyfin plugin that scrobbles your watch history to [Marquee](https://traktlite.vercel.app).
When you finish a movie or episode in Jellyfin — or mark something as played —
it is recorded as watched in your Marquee profile.

## How it works

- The plugin runs inside your Jellyfin server and listens for playback and
  user-data events.
- Each Jellyfin account is paired with a Marquee account via a short
  confirmation code (device-code style): the plugin asks Marquee for a code,
  you confirm it on the Marquee website while logged in, and the plugin
  receives a per-account scrobble token.
- Finished watches are POSTed to `POST /api/jellyfin/plugin/scrobble` on your
  Marquee instance with that token.

## Building

Requires the [.NET SDK](https://dot.net) and
[JPRM](https://github.com/oddstr13/jellyfin-plugin-repository-manager)
(`pip install jprm`).

The project multi-targets three Jellyfin API lines:

| Framework | Jellyfin        |
| --------- | --------------- |
| net8.0    | 10.10.x (LTS)   |
| net9.0    | 10.11.x         |
| net10.0   | 12.x            |

Build a single variant:

```bash
cp build.stable.yaml build.yaml   # or build.lts.yaml / build.v12.yaml
jprm plugin build . --output=./dist --dotnet-configuration=Release
```

This produces a `.zip` you can upload manually in
**Jellyfin Dashboard → Plugins → Repositories → Install from file** (the
"Manual Installation" option), or host the zips + `manifest.json` as a custom
plugin repository so users install it like any catalog plugin.

## Configuration

Open **Jellyfin Dashboard → Plugins → Marquee**:

1. Set the **Marquee server URL** (defaults to `http://localhost:3000` for
   development — set it to your deployed Marquee URL).
2. Pick a Jellyfin account and click **Connect to Marquee**.
3. Confirm the short code shown on the Marquee site while logged in.
4. Done — watched marks flow automatically.

Each Jellyfin user on a shared server links to their own Marquee account.
Accounts that are not linked scrobble nothing.

## Development

```bash
# Build all target frameworks
dotnet build Jellyfin.Plugin.Marquee.sln -c Release
```

## License

MIT
