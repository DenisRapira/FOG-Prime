# FOG Prime architecture

FOG Prime is split into three local components:

| Component | Responsibility |
| --- | --- |
| `FOG Prime.exe` | Minimal user interface; never receives engine arguments or starts shell scripts. |
| `FOG.Agent.exe` | Local supervisor; owns profile selection, trust verification, VPN/EAC compatibility, process lifecycle, state, and connectivity checks. |
| `FOG.Engine.exe` | Network runtime compiled from the pinned source revision in `FOG.Engine/upstream`. |

The UI and Agent communicate through the current-user named pipe `fog-prime-agent-v3`. Every request carries a random per-launch session token and correlation ID. The protocol accepts only `ping`, `status`, `start`, `stop`, `recheck`, and `shutdown`; arbitrary executable paths and arguments are not part of the protocol.

Before opening IPC, Agent compares packaged profiles and `runtime.manifest.json` with trusted copies embedded at build time, then validates every runtime file with SHA-256. The UI also validates the packaged Agent before starting it. Engine profiles start only through `ProcessStartInfo.ArgumentList`, never via `cmd.exe`, PowerShell, or `.bat` files.

Agent detects protected games without changing or restarting a live validated Engine profile. When an external VPN/tunnel is active, Agent stops Engine and uses direct mode so FOG does not layer WinDivert over the tunnel. Connectivity recovery uses repeated probes, bounded driver-conflict backoff, and reserve profiles only after a real Engine process failure.

The portable UI owns the Agent process lifetime. Closing the UI sends authenticated `shutdown`, then cleans up only Agent and Engine processes from the same package directory. The latest health snapshot is stored atomically under `%LOCALAPPDATA%\FOG Prime\state.json`.
