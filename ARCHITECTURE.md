# FOG Prime architecture

FOG Prime is split into three local components:

| Component | Responsibility |
| --- | --- |
| `FOG Prime.exe` | Minimal user interface; never receives engine arguments or starts shell scripts. |
| `FOG.Agent.exe` | Local supervisor; owns profile selection, runtime verification, process lifecycle, persistent state, and Discord health checks. |
| `FOG.Engine.exe` | Network runtime compiled from the pinned source revision in `FOG.Engine/upstream`. |

The UI and Agent communicate through the local named pipe `fog-prime-agent-v1`. The pipe protocol accepts only `status`, `start`, `stop`, and `recheck`; arbitrary executable paths and arguments are not part of the protocol.

Before start, Agent validates every runtime file against `runtime.manifest.json` with SHA-256. It starts an engine profile only through `ProcessStartInfo.ArgumentList`, never via `cmd.exe`, PowerShell, or `.bat` files. Agent stores its latest health snapshot atomically under `%ProgramData%\FOG Prime\state.json`.

For persistent operation, `build/install-agent-service.ps1` installs Agent as a Windows service with automatic startup and three restart attempts. The portable UI can also launch the packaged Agent quietly when the service is not installed.
