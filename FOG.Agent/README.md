# FOG Agent

Local .NET supervisor for the unpublished 2.0.3/.NET 10 candidate. Published stable 2.0.2 uses .NET 8; see [release status](../docs/release-status.json).

The packaged UI starts `FOG.Agent.exe` with a per-session `FOG_PRIME_PIPE_NAME` (`fog-prime-session-` plus 32 hex digits) and random 256-bit `FOG_PRIME_SESSION_TOKEN`. Agent consumes and clears both environment variables. It does not accept the legacy fixed pipe as a session name. Use the packaged UI for normal startup; launching Agent alone without packaging trust resources and session values is not a supported shortcut.

Commands are limited to `ping`, `status`, `start`, `stop`, `recheck`, `shutdown`, `game-filter-on` and `game-filter-off`. Agent validates embedded profiles/runtime metadata and file hashes before serving IPC, supervises Engine, runs synthetic DNS/HTTPS/Gateway probes, and reads recent local Discord connection-state evidence when available. These checks do not prove audio, NAT or anti-cheat compatibility.

The manual game filter defaults off, persists locally, and uses the shared ROB RUN/Warframe catalog. Discord/game launch does not start FOG or enable the filter. Filter rules use address lists, not per-process isolation. A healthy detected tunnel can use direct mode after fresh probes with Engine stopped; a degraded tunnel can cause Engine to start alongside it. Process/adapter detection remains heuristic and live driver/VPN transitions need controlled testing.

State and settings are under `%LOCALAPPDATA%\FOG Prime`: `state.json`, `preferred-profile.json`, `game-filter.json` and `engine-session.json`. UI captures Agent output into that folder's `logs` subdirectory. Diagnostics can contain personal network information; see [Privacy](../docs/PRIVACY.md).

The portable UI owns the normal process lifetime. Service-host support is named `FOG Prime Agent`, but the portable package does not install that service. Driver cleanup separately targets an ownership-checked `WinDivert` service; `BFE` is a Windows prerequisite, not a FOG service.

Start/Stop running intent changes are serialized under the supervisor gate; a cancelled gate waiter does not change intent. Probe results and cached successes are checked against network identity and a topology generation before use. Changed topology discards stale evidence and requests fresh checks without replacing an established Engine solely for that change; cancelled in-flight checks do not publish results. These are regression-tested control-flow properties, not live VPN validation.

Use [Building](../docs/BUILDING.md) for SDK selection, trusted packaging and integration-test restrictions. A plain solution build is not a complete trusted distribution. As of 2026-11-27, the candidate Release build completed with zero warnings/errors and 97/97 scoped tests passed. A fresh minimal package still requires exact-artifact acceptance; earlier candidate hashes are historical. Live lifecycle/driver/VPN tests have not been run and the full native audit remains incomplete. See [Architecture](../ARCHITECTURE.md) and [distribution obligations](../docs/LICENSE-COMPLIANCE.md).
