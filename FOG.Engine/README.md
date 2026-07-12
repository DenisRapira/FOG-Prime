# FOG Engine

`FOG.Engine` is the Windows network runtime used by FOG Prime Agent.

The source is pinned in `upstream/`; it is not replaced by a downloaded binary at package time. Build an x64 runtime with the GitHub Actions workflow or a local Cygwin environment, then package it with:

```powershell
.\build\package.ps1 -EngineRuntime .\FOG.Engine\artifacts\win-x64 -Version 0.1.0
```

The runtime directory must contain `FOG.Engine.exe` and all of its runtime dependencies, including the matching WinDivert DLL and driver. `package.ps1` generates SHA-256 integrity metadata for every runtime file.

See `UPSTREAM.md` and `THIRD_PARTY_NOTICES.md` for required notices.
