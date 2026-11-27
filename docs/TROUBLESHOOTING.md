# Troubleshooting

## FOG Prime reports that components are missing

Extract the complete release ZIP. Do not move only `FOG Prime.exe`; it requires `FOG.Agent.exe`, `profiles.json`, `runtime.manifest.json`, and the `runtime` directory.

## Windows blocks startup

Check the downloaded archive properties, Windows Security history, and whether the WinDivert driver remains in `runtime`. A driver signature or SHA-256 match is not an antivirus verdict, and application signature trust must be checked separately. Do not disable security software, add broad exclusions or install certificates to bypass warnings. No clean antivirus result is available from the supplied audit record: Defender was stopped, the VirusTotal API key was unavailable, and file uploads were not authorized.

## A connectivity check fails

Record the exact application version, retry once and review redacted diagnostics. A retry does not promise to test every profile: an established Engine session may be preserved. Corporate network policy may intentionally prevent this software from operating. Check Windows Base Filtering Engine (`BFE`) without changing firewall or security settings.

The candidate can use direct mode when a detected external tunnel passes fresh probes with Engine stopped. A degraded tunnel may cause Engine to start alongside it; detection alone does not guarantee isolation. Do not stop a required VPN or other packet filter without understanding its role. Live transitions still require controlled VM validation.

The latest snapshot is `%LOCALAPPDATA%\FOG Prime\state.json`, not `%ProgramData%`. UI and captured Agent diagnostics are in `%LOCALAPPDATA%\FOG Prime\logs`. Redact paths, adapter names, IP addresses, private domains and tokens before sharing. See [Privacy](PRIVACY.md).

## Discord voice or games still fail

Current probes check synthetic Gateway/HTTPS responses and recent local Discord connection-state entries when available. `RTC_CONNECTED` is not an audibility test. No authenticated voice-media/audio loopback, NAT repair, latency reduction or anti-cheat safety is guaranteed.

The candidate's manual game filter defaults off and covers ROB RUN/Warframe through address lists, not process identity. Shared CDN networks can include other applications. Opening Discord or a game does not launch FOG or enable the filter. Toggling it can reconnect Engine, not restart the game. Protected-process detection is a limited heuristic, not EAC certification.

## A test script fails before startup

`test-startup-reliability.ps1` is retired and deliberately fails before launching or stopping processes; it is not current acceptance evidence. `test-ui-lifecycle.ps1` requires `-AllowLiveNetworkTests`, refuses existing FOG instances, scopes cleanup to package EXE paths and reads `%LOCALAPPDATA%\FOG Prime\state.json`. As of 2026-11-27, this live lifecycle test has not been run. See [Building](BUILDING.md) and [Patch and Release Acceptance](PATCH-CHECKLIST.md); a deliberate opt-in refusal is not a runtime regression.

## Clean removal

Close the FOG Prime window and confirm that processes from that package have exited before removing its extracted folder. Do not terminate another installation by name alone. The portable package does not install a persistent Agent service. Agent's optional Windows-service host name is `FOG Prime Agent`; the owned driver service cleanup targets `WinDivert`, not `BFE`. Do not delete arbitrary driver services or manually remove a loaded driver.

Removing the extracted folder does not erase `%LOCALAPPDATA%\FOG Prime` (state, logs, profile/filter settings, session metadata and WebView2 profile). After all FOG/WebView2 processes for this package have exited, that application-data folder can be removed to erase local settings and diagnostics. It is shared by installations under the same user, so preserve needed data first.
