# FOG Engine

`FOG.Engine` is the Windows network runtime used by FOG Prime Agent.

This working tree belongs to the unpublished 2.0.3 candidate. Stable 2.0.2 and candidate status are recorded in [release-status.json](../docs/release-status.json); the managed candidate targets .NET 10, while this Engine is native C built with Cygwin.

The source is pinned in `upstream/`; it is not replaced by a downloaded binary at package time. Build an x64 runtime with the GitHub Actions workflow or a local Cygwin environment, then package it with:

```powershell
.\build\package.ps1 -EngineRuntime .\FOG.Engine\artifacts\win-x64 -Version 2.0.3
```

The runtime directory must contain `FOG.Engine.exe` and all of its runtime dependencies, including the matching WinDivert DLL and driver. `package.ps1` generates SHA-256 integrity metadata for every runtime file.

The candidate package copies only allowlisted runtime binaries/profile references and verified bundled assets, not the whole runtime input directory. The unused `discord-ip-discovery-with-port.bin` sample is omitted for current profiles. Root `payload-provenance.json`, `game-filter-provenance.json`, `build-info.json`, configuration and integrity metadata remain in the package.

Run from the repository root and pass the required, validated `-Version` explicitly; there is no default version. Runtime preparation downloads official WinDivert 2.2.2 binaries and verifies the archive; the Engine itself is compiled from retained source. See [Building](../docs/BUILDING.md) for prerequisites and packaging order. Hashes are not an antivirus verdict or a publisher signature.

Agent starts approved Engine profiles directly. QUIC/Discord payloads and the native SSRC patch do not constitute an end-to-end voice test, NAT fix, game latency guarantee or anti-cheat certification. The manual game filter is address-list-based and may affect other applications sharing a CDN. As of 2026-11-27, a full native-code audit remains incomplete; driver VM tests and live VPN transitions have not been run.

Retain [UPSTREAM.md](UPSTREAM.md), [Engine notices](THIRD_PARTY_NOTICES.md), [root notices](../THIRD_PARTY_NOTICES.md) and license texts. Original application MIT terms do not replace dependency terms. The [distribution checklist](../docs/LICENSE-COMPLIANCE.md) still requires exact corresponding-source access for distributed WinDivert/Cygwin binaries and packet/list asset licensing review. Attribution alone is not permission or a compliance certification.

Those links refer to source-tree documents. In the minimal candidate ZIP, Engine notices are `notices/ENGINE-NOTICES.md`, upstream provenance is `notices/UPSTREAM.md`, and dependency licenses are also under `notices/`. Root `LICENSE` and `THIRD_PARTY_NOTICES.md` remain at the package root. Internal reports and the distribution checklist are not copied into the ZIP.
