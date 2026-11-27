# FOG Prime architecture

This describes the unpublished 2.0.3/.NET 10 candidate in the working tree, not a verification of the published 2.0.2/.NET 8 binary. FOG Prime is split into three local components:

| Component | Responsibility |
| --- | --- |
| `FOG Prime.exe` | Minimal user interface; never receives engine arguments or starts shell scripts. |
| `FOG.Agent.exe` | Local supervisor; owns profile selection, integrity checks, tunnel/protected-process detection heuristics, lifecycle, state and connectivity probes. Detection is not a compatibility guarantee. |
| `FOG.Engine.exe` | Network runtime compiled from the pinned source revision in `FOG.Engine/upstream`. |

The UI creates a current-user-only named pipe name `fog-prime-session-<32 hex digits>` and a random 256-bit session token for each Agent launch. It supplies them through `FOG_PRIME_PIPE_NAME` and `FOG_PRIME_SESSION_TOKEN`; Agent reads and clears these environment variables. The UI verifies the pipe server PID against the Agent it started. The old `fog-prime-agent-v3` constant is not an accepted session pipe name. Requests carry a session token and correlation ID. The command set is `ping`, `status`, `start`, `stop`, `recheck`, `shutdown`, `game-filter-on`, and `game-filter-off`; arbitrary executable paths and arguments are not part of the protocol.

Before opening IPC, Agent compares packaged profiles and `runtime.manifest.json` with trusted copies embedded during packaging, then validates every runtime file with SHA-256. The UI validates the packaged Agent against an embedded hash before starting it. Candidate runtime leases reject reparse points and hold file/directory handles to restrict replacement. A plain solution build is not a complete runnable trusted package. Hash checks establish consistency with the build's embedded metadata, not publisher identity or an antivirus verdict. Engine profiles start through `ProcessStartInfo.ArgumentList`, not shell scripts; driver-service cleanup separately invokes Windows `sc.exe`.

Agent detects a limited list of Easy Anti-Cheat process names; that signal does not certify EAC safety. It preserves an established Engine session despite ancillary probe failures. During initial/pending profile selection, repeated failed probes can still cause rotation while the process is alive; process exit also permits recovery. Driver-conflict retries use bounded backoff.

External tunnel detection uses adapter type/name heuristics. Direct mode requires fresh successful probes with Engine stopped. If a detected tunnel fails those checks, the candidate can start Engine alongside it; tunnel appearance alone does not always stop WinDivert. Network events trigger reevaluation and profile memory is keyed by a hash of active adapter/network details, profiles, game-filter setting and catalog fingerprint. Live VPN handoffs and driver behavior still need controlled VM testing.

The UI normally requests connection when FOG opens. Opening Discord or a game does not start FOG or enable the game filter. The manual filter defaults off, persists in `game-filter.json`, and covers the shared ROB RUN/Warframe catalog. Enabling it adds address-list-based TCP/UDP rules for ports 1024-65535, excluding listed local networks. This is not per-process isolation and shared CDN addresses can affect other applications. It does not guarantee NAT, voice, latency or anti-cheat compatibility.

Connectivity evidence consists of DNS/HTTPS probes, synthetic Discord Gateway HELLO/heartbeat ACK, and recent connection-state entries read from the local stable Discord renderer log when available. No microphone/audio loopback or authenticated voice-media test is performed. QUIC rules and payloads in Engine are not proof that real QUIC or audio works.

The portable UI owns the Agent process lifetime. Closing the UI sends authenticated `shutdown`, then cleans up only Agent and Engine processes from the same package directory. The latest health snapshot is stored atomically under `%LOCALAPPDATA%\FOG Prime\state.json`.

The normal portable package does not install a persistent Agent service. `Program.cs` supports a Windows-service host named `FOG Prime Agent`, but that is not the UI launch path. The driver service managed by ownership-checked cleanup is `WinDivert`; Windows Base Filtering Engine is `BFE`. State, profile memory, logs and `WebView2Profile` remain after the extracted folder is removed. See [Privacy](docs/PRIVACY.md) for local data and external requests, including the automatic GitHub update check.
