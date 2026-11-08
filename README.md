# Codex Usage Monitor

A Windows system tray app built with .NET 10 and Windows Forms. It reads Codex account rate limits on startup and at a configurable interval (60 seconds by default), and displays the current usage in the tray icon's hover tooltip.

## Run

Requirements: Windows, the .NET 10 Desktop Runtime (or SDK for development), and an installed Codex CLI signed in with a ChatGPT account. Sign in using `codex login` if needed.

```powershell
dotnet run --project CodexUsageMonitor --configuration Release
```

The app starts directly in the notification area with no main window or console. If Windows hides the icon, look under the taskbar's overflow arrow. Only one instance runs per Windows session.

- Hover over the icon for usage percentages, quota window lengths, local reset times, and time remaining until reset.
- The circular icon has two gauges: **left = 5-hour quota**, **right = 1-week quota**. In each half, color shows remaining quota and dark gray shows used quota.
- Right-click for **Refresh now**, **Usage details...**, and **Exit**.
- Double-click the icon to open the details window, including all returned usage buckets, credits, and plan information. Closing this window leaves the monitor running.
- Failed checks show a gray icon with an **X**. The last successful reading is retained and marked **STALE** until a check succeeds. Errors and sign-in guidance appear in the details window.
- Usage threshold crossings display a Windows notification. Click the notification to open usage details.

Windows limits the native tooltip to 127 characters. The tooltip prioritizes the `codex` bucket and shows the additional bucket count when space permits. Plan and update metadata are omitted when necessary to make room for the reset countdowns. The details window always includes all returned buckets. Missing usage windows are shown as unavailable, never as zero usage.

Reset times include a countdown, for example `reset 11/06 15:30 (2h 30m left)`. The details window also includes countdowns for quota windows and individual spend limits. Countdowns update every minute from the current clock, independently of the configured data retrieval interval and without additional server requests. They use days and hours, hours and minutes, or minutes; less than a minute appears as `<1m left`. A timestamp at or before the current time is marked `due` until a later usage reading supplies a new reset time. Unknown or invalid reset times remain `unknown`.

## Remaining-quota pie icon

The icon updates after each automatic or manual refresh. A vertical gap separates two independent semicircular gauges: **left is the primary window (normally 5 hours)** and **right is the secondary window (normally 1 week)**. Each half uses its own quota as 100%: a fully colored half means all of that quota remains; half of a semicircle colored means 50% remains; an empty outlined half means 0%. The dark sector represents consumed quota.

Both colored sectors start at twelve o'clock and extend outward around their respective halves: counterclockwise on the left and clockwise on the right. Fractional percentages are preserved. When multiple buckets are returned, each half uses the highest valid usage for its corresponding window across those buckets; primary and secondary values are never combined. Other window durations retain their primary/secondary positions, with the actual durations shown in the tooltip and details.

The open details window uses the same icon. Each half independently follows its **used** percentage and the semantic states configured in `iconStates`. State names describe severity; their colors are customizable:

| State | Default color | Meaning |
| --- | --- | --- |
| Normal | Green, `#168C4C` | Below `elevated.thresholdPercent` |
| Elevated | Amber, `#CA9100` | At or above the elevated threshold and below high |
| High | Orange, `#E86C0C` | At or above the high threshold and below critical |
| Critical | Red, `#CC3030` | At or above the critical threshold, 100% usage, or a server-reported usage/spend limit |
| Unknown | Gray, with **…** | Waiting for the first reading, or no valid usage percentages provided |
| Error | Gray, with **X** | The most recent refresh failed; cached usage may be stale |

For example, elevated/high/critical thresholds of 50/80/95 select normal below 50%, elevated from 50% to below 80%, high from 80% to below 95%, and critical at 95% or higher. Each state uses its own configured color, including the visible outline when a quota is empty. Critical takes precedence over other usage states. These boundaries and colors are independent of the number and values of notification thresholds.

An empty `notificationThresholdsPercent` list disables toast notifications without changing the configured icon colors. Each half grows and returns to lower usage colors when a new reading reports lower usage for that window. A missing or invalid window is shown as a gray half with a dash, preserving the other half's reading. If both windows are unavailable, the icon shows gray **…**; failed retrievals show a gray **X**. These states never imply empty or full quota.

An explicit server-reported account or spending limit makes both halves critical while retaining each window's reported remaining percentage. A **!** on the configured critical-color background replaces a half with no percentage data; if neither percentage is available, the icon displays a single **!** in that color.

The active icon is reused for identical readings. When either half's color or remaining percentage changes, both the tray and details window switch to the new icon before the old native icon is released. Only the active icon is retained, and it is released on exit.

## Settings

At startup, the app reads `settings.json` from the directory containing the executable. The file is included in build and publish output. Edit it and restart the app to change the refresh interval, icon colors, or notification thresholds; for example:

```json
{
  "refreshIntervalSeconds": 120,
  "iconStates": {
    "normal": { "color": "#168C4C" },
    "elevated": { "thresholdPercent": 50, "color": "#CA9100" },
    "high": { "thresholdPercent": 80, "color": "#E86C0C" },
    "critical": { "thresholdPercent": 95, "color": "#CC3030" }
  },
  "notificationThresholdsPercent": [50, 75, 90, 95, 100]
}
```

`refreshIntervalSeconds` must be a JSON integer from **1** through **2,147,483** seconds (the Windows timer's maximum supported whole-second interval). Missing values, zero, negative or fractional values, wrong types, and values outside this range fall back to **60 seconds**.

`iconStates` contains `normal`, `elevated`, `high`, and `critical`. The last three each accept an integer `thresholdPercent` and a `color`. Normal is the state below elevated and accepts only a `color`; it does not need a threshold. Thresholds must satisfy **1 ≤ elevated < high < critical ≤ 100**. A missing or invalid threshold falls back to that state's default (80, 90, or 100); if the resulting order is invalid, all three thresholds revert to 80/90/100. Named thresholds are never reordered.

Colors use opaque six-digit hexadecimal codes, **`#RRGGBB`**, with either uppercase or lowercase letters. Each missing or invalid color falls back to its own default from the table above, preserving other valid colors and thresholds. Partial state objects are allowed, so a color can be customized without specifying its threshold. Invalid threshold ordering does not discard valid custom colors. Changes take effect after restarting the app.

`notificationThresholdsPercent` accepts any number of integer percentages from **1** through **100**. The default is **[80, 90, 100]**. The app sorts the list, removes duplicates, and ignores invalid entries. A missing or incorrectly typed list, or a nonempty list containing no valid values, uses the default notification thresholds. Set it to **[]** to disable usage notifications while retaining the configured icon colors.

Each setting is validated independently. A missing, unreadable, or malformed JSON file uses all defaults. Unknown settings keys are ignored.

For compatibility, when `iconStates` is absent, the former `iconColorThresholdsPercent` object is accepted: `amber`, `orange`, and `red` map to elevated, high, and critical, using the original palette. Its three fields must still be valid and strictly increasing, or the default thresholds are used. If that object is also absent, the older `usageThresholdsPercent` list supplies its first, second, and last normalized values as the severity thresholds when at least three values remain; shorter lists use the default thresholds. The legacy list also supplies notification thresholds when `notificationThresholdsPercent` is absent.

An explicit `iconStates` section takes precedence over both older icon formats; missing or invalid fields in it use the new defaults rather than legacy values. An explicit `notificationThresholdsPercent` list likewise takes precedence, including `[]` to disable notifications. The app does not rewrite legacy files.

The initial reading still happens immediately, and manual refresh remains available. Settings are loaded once at startup; the app does not rewrite the file. When running from source, edit `CodexUsageMonitor/settings.json` before building or running.

## Usage notifications

The `notificationThresholdsPercent` thresholds apply independently to the primary and secondary windows of every returned usage bucket. A reading at or above a threshold triggers an alert, including the first reading after starting the app. If several thresholds or windows cross in one refresh, they are combined into one notification with the current usage and crossed percentages. Changing icon state thresholds or colors does not change which notifications are triggered.

Repeated readings above a threshold do not repeat the alert. An alert becomes eligible again after usage falls below its threshold, the server reports a new quota window, or an identified account changes. Missing windows and failed refreshes preserve the previous alert state. This state is held in memory, so restarting the monitor allows alerts for already-reached thresholds again.

Notifications use the existing tray icon through `NotifyIcon.ShowBalloonTip`. Windows displays these as native notification banners; on Windows 11 they are transient rather than retained in Notification Center. Windows notification settings determine whether banners are shown. See [Microsoft's tray notification documentation](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw). No additional notification runtime or registration is required.

## Codex connection

The monitor starts an owned `codex app-server --listen stdio://` process, completes the `initialize` / `initialized` handshake, then calls `account/rateLimits/read`. The protocol follows the [official Codex app-server documentation](https://learn.chatgpt.com/docs/app-server). This endpoint reports quota-window usage, not token counts.

The process uses the existing Codex authentication and configuration, including `CODEX_HOME` if set. The app does not read or copy credentials, initiate model turns, or require an OpenAI API key. If authentication is unavailable or unsupported, sign in with ChatGPT through the CLI and choose **Refresh now**; failed connections are restarted automatically.

Discovery checks `PATH`, the standard Codex desktop CLI location, and the npm command directory. For a custom installation, set the path before starting the app:

```powershell
$env:CODEX_USAGE_MONITOR_CODEX_PATH = 'C:\Tools\Codex\codex.exe'
dotnet run --project CodexUsageMonitor --configuration Release
```

Native `.exe` and npm `.cmd` launchers are supported. Requests have a 30-second timeout, refreshes never overlap, and the owned server is shut down when the app exits.

## Data and extension points

- `CodexUsageMonitor.Core/Models/UsageSnapshot.cs`: immutable C# `record` types for timestamped readings, quota windows, multiple buckets, credits, and spend limits. Collections are immutable, nullable fields remain optional, and plan identifiers are strings to support future plans.
- `IRateLimitClient` / `CodexRateLimitClient`: asynchronous account reading, independent of Windows Forms.
- `AppServerProtocol`: newline-delimited JSON request/reply handling, including interleaved notifications.
- `UsageTextFormatter`: tooltip and detailed text rendering, including time-zone conversion and length limits.
- `UsageIconClassifier` / `UsageIconAppearance` / `UsageWindowAppearance`: map current usage, configured thresholds, and refresh failures to independent primary and secondary icon halves.
- `IconUsageThresholds` / `IconStateColors`: validated severity boundaries and RGB drawing colors, independent of Windows drawing APIs and notification thresholds.
- `UsageThresholdMonitor`: independent threshold tracking per usage window, producing immutable alert records for the tray notification UI.
- `TrayApplicationContext`: polling and tray lifecycle, retaining the most recent successful record in memory. Persistent history is not enabled; a history repository can be added without changing the transport or data models.

There are no third-party runtime dependencies. Launching at Windows sign-in is optional: place a shortcut to the published executable in your Windows Startup folder.

## Build and test

```powershell
dotnet build CodexUsageMonitor.slnx --configuration Release
dotnet test CodexUsageMonitor.slnx --configuration Release
dotnet publish CodexUsageMonitor/CodexUsageMonitor.csproj --configuration Release --output .artifacts/publish
```

Run `.artifacts/publish/CodexUsageMonitor.exe` after publishing. This is a framework-dependent build requiring the .NET 10 Desktop Runtime.

Automated tests cover settings validation and fallback, remaining-quota calculations, icon color boundaries and stale-data handling, threshold crossings and duplicate suppression, account and window resets, notification text limits, response deserialization, missing fields, multiple buckets, tooltip limits, reset times, the initialization handshake, server errors, and process lifetime. A local fake app-server exercises pipe buffering, sequential requests, timeouts, cancellation, and reconnects without accessing an account or the network.
