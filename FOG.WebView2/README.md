# FOG Prime WebView2 UI

WPF/WebView2 desktop UI for the unpublished 2.0.3/.NET 10 candidate. Published stable 2.0.2 uses .NET 8; [release-status.json](../docs/release-status.json) is the canonical status record.

## Startup

Run `FOG Prime.exe` from a complete extracted official package, or from the output of [trusted packaging](../docs/BUILDING.md). The documented local packaging destination is `release/FOG-Prime` relative to the repository root, not `FOG.WebView2/release`. A plain `dotnet build` does not create a runnable trusted distribution.

The manifest requests administrator elevation for the network runtime. The UI checks Windows, WebView2, privileges and required Agent/runtime files; it does not require PowerShell or curl to run connection probes. The managed applications are packaged self-contained; WebView2 Runtime is still a separate prerequisite.

Opening FOG normally requests connection and shows a single overall status. Agent owns the bundled profile search and recovery; the UI does not promise four strategies or expose arbitrary shell commands. Closing the window requests authenticated shutdown and cleans up owned package processes. WebView2 recovery preserves the existing Agent session where possible.

The manual game-filter toggle defaults off and persists through Agent. It applies the shared ROB RUN/Warframe catalog, not process-based isolation. Opening Discord or a game does not launch FOG or switch the filter on. A green status reflects synthetic probes and any recent desktop-log evidence, not guaranteed audible voice, video, NAT or anti-cheat safety.

## Diagnostic Startup

From a repository root with an existing package at the example output location:

```powershell
$env:FOG_PRIME_NO_AUTOSTART = '1'
try {
    & '.\release\FOG-Prime\FOG Prime.exe'
}
finally {
    Remove-Item Env:FOG_PRIME_NO_AUTOSTART
}
```

This suppresses automatic connection startup only. It does not disable the automatic GitHub Releases API update check or guarantee zero network requests. The checker opens an official release page after user action; it does not install updates. See [Privacy](../docs/PRIVACY.md) for external requests, local Discord log reads, diagnostics and the persistent WebView2 profile.

UI/Agent logs are under `%LOCALAPPDATA%\FOG Prime\logs`; state is in the same parent folder's `state.json`. Redact network information before sharing. The startup-reliability script is retired, not a candidate acceptance test; live UI tests require explicit authorization. See [Building](../docs/BUILDING.md).
