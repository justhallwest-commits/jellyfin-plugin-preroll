# Pre-Roll Videos — Jellyfin Plugin

Plays a randomly selected pre-roll video before **movies and TV episodes** across all your Jellyfin clients.

## Client Support

| Client | Engine | Notes |
|--------|--------|-------|
| Jellyfin Web (browser) | `IIntroProvider` | Full support |
| Jellyfin iOS / Android | `IIntroProvider` | Full support |
| Jellyfin Media Player (desktop) | `IIntroProvider` | Full support |
| Swiftfin / Infuse | `IIntroProvider` | Full support |
| Roku | Session Interceptor | Playlist injection |
| Fire TV / Android TV | Native `PreRollPath` | Workaround — see below |

### Fire TV Workaround

Fire TV clients crash when a `SendPlayCommand` is sent externally. Instead, the plugin dynamically sets Jellyfin's built-in `Server Configuration → Pre-Roll Video Path` to the chosen pre-roll before each playback session. This works because Jellyfin prepends the video at the transcode layer. The path is automatically reset after 3 minutes.

> **Note:** Fire TV pre-rolls only work on transcoded streams. If your Fire TV uses direct play for everything, the pre-roll may not appear. Set a transcode quality cap in your Jellyfin settings for Fire TV if needed.

---

## Installation

### From ZIP (manual)

1. Download `jellyfin-plugin-preroll.zip` from the [Releases](../../releases) page.
2. Extract the DLL to your Jellyfin plugins folder:
   - **Unraid (Docker `jellyfin-1`):** `/mnt/user/appdata/jellyfin/plugins/PreRoll_1.0.0.0/`
3. Restart Jellyfin.
4. Go to **Dashboard → Plugins → Pre-Roll Videos** to configure.

### From Plugin Repository (recommended)

Add this URL to your Jellyfin plugin repositories:
```
https://raw.githubusercontent.com/justhallwest-commits/jellyfin-plugin-preroll/main/manifest.json
```

Then install **Pre-Roll Videos** from the catalog.

---

## Configuration

1. **Create a Jellyfin library** for your pre-roll videos:
   - Go to **Dashboard → Libraries → Add Media Library**
   - Type: `Videos` (or `Movies`)
   - Point it at your pre-roll folder (e.g. `/mnt/user/media/pre-rolls/`)
   - Let it scan.

2. **Open the plugin config page** at Dashboard → Plugins → Pre-Roll Videos:
   - Under **Pre-Roll Source Library**, select the library you just created.
   - Under **Play Pre-Rolls Before…**, check Movies, TV Episodes, or both.
   - Under **Apply To…**, choose all libraries or pick specific ones.
   - Click **Save Settings**.

3. Play something — the plugin picks a random video from your pre-roll library each time.

---

## Building from Source

```bash
git clone https://github.com/justhallwest-commits/jellyfin-plugin-preroll.git
cd jellyfin-plugin-preroll
dotnet build Jellyfin.Plugin.PreRoll/Jellyfin.Plugin.PreRoll.csproj --configuration Release
```

Requires .NET 9 SDK.

---

## Architecture

```
┌────────────────────────────────────────────────────────────┐
│                   Pre-Roll Videos Plugin                   │
│                                                            │
│  ┌─────────────────────┐   ┌──────────────────────────┐   │
│  │  PreRollIntroProvider│   │    SessionInterceptor     │   │
│  │  (IIntroProvider)   │   │    (IHostedService)       │   │
│  │                     │   │                           │   │
│  │  Web / iOS /        │   │  Roku → PlayNow playlist  │   │
│  │  Android / Desktop  │   │  Fire TV → PreRollPath    │   │
│  └─────────────────────┘   └──────────────────────────┘   │
│               ↓                          ↓                 │
│          ┌────────────────────────────────┐                │
│          │         PreRollManager         │                │
│          │  • GetRandomPreRoll()          │                │
│          │  • ShouldPlayPreRoll(item)     │                │
│          │  • IsPreRollItem(item)         │                │
│          └────────────────────────────────┘                │
└────────────────────────────────────────────────────────────┘
```

---

## Compatibility

- **Jellyfin:** 10.11.6
- **.NET:** 9
- **Unraid Docker image:** `jellyfin/jellyfin:latest` (`jellyfin-1`)
