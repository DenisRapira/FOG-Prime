# FOG Prime architecture

FOG Prime is split into three local components:

| Component | Responsibility |
| --- | --- |
| `FOG Prime.exe` | Minimal user interface; never receives engine arguments or starts shell scripts. |
| `FOG.Agent.exe` | Session-bound local supervisor; owns profile selection, runtime verification, process lifecycle, persistent state, and Discord/YouTube health checks. |
| `FOG.Engine.exe` | Network runtime compiled from the pinned source revision in `FOG.Engine/upstream`. |

The UI starts its own Agent and communicates through `fog-prime-agent-v3`. The pipe is restricted to the current Windows user and every request carries a random 256-bit session token. The protocol accepts only `ping`, `status`, `start`, `stop`, `recheck`, and `shutdown`; arbitrary executable paths and arguments are not part of the protocol. Old v1/v2 Agents and stale FOG processes are removed before a new session starts.

The packaged UI contains the trusted SHA-256 of `FOG.Agent.exe`. Agent contains trusted copies of `profiles.json` and `runtime.manifest.json`; it compares external files with those copies before validating every runtime file. It starts an Engine profile only through `ProcessStartInfo.ArgumentList`, never via `cmd.exe`, PowerShell, or `.bat` files. Agent stores each command result atomically under `%ProgramData%\FOG Prime\state.json`.

The portable UI owns the complete process lifetime. Closing the window sends an authenticated shutdown, waits for Agent, and removes remaining FOG processes. Persistent Windows-service operation is intentionally unsupported.
