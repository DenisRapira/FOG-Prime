# Product Direction - 2026-11-27

Read-only primary-source research by three reviewers plus local packaging
experiments. This is an engineering/product landscape, not a measured market
share survey. Proposals below are not already delivered features. No public
release, engine migration or host-network reconfiguration was performed.

## Relevant Current Signals

- Flowseal stable is 1.10.4 (2026-10-20). Its 2.0.0-rc, published
  2026-11-27 17:37:59 UTC, is explicitly a prerelease testing a new core.
  [Stable](https://github.com/Flowseal/zapret-discord-youtube/releases/tag/1.10.4),
  [RC](https://github.com/Flowseal/zapret-discord-youtube/releases/tag/2.0.0-rc).
- The migration is a strategy/runtime transition, not merely a renamed EXE:
  [pinned migration commit](https://github.com/Flowseal/zapret-discord-youtube/commit/cdaf1b6a81da1c424eea744995281e4636ee3b9a).
  zapret2's current release is v1.0.8, 2026-11-25; its architecture moves
  strategy logic into Lua. [Release](https://github.com/bol-van/zapret2/releases/tag/v1.0.8),
  [design](https://github.com/bol-van/zapret2).
- ByeDPI documents failure-driven strategy groups and caching, but is a local
  SOCKS proxy rather than a drop-in transparent Windows packet-layer backend.
  [Version-pinned documentation](https://github.com/hufrea/byedpi/blob/v0.17.3/README.md).
- Inference: replaceable, bounded adaptation is a more durable direction than
  assuming one permanent preset. These examples do not prove a universal
  bypass, popularity ranking or reliable Discord/game media support.

FOG already has network-context profile memory, adaptive polling, cached
snapshots, event coalescing, retained integrity leases and topology-generation
validation. Do not present those as future features or add duplicate layers.

## Prioritized Work

| Priority | New work | Value and acceptance gate |
| --- | --- | --- |
| 1 | Lightweight UI-host prototype, retaining current HTML and Agent | Compare native Win32/WebView2 hosting against WPF without changing the Engine. Measure entire process-tree memory, startup p50/p95, extracted/download/cache size; preserve all IPC, navigation, accessibility, crash and shutdown protections. No size target is promised before measurement. |
| 1 | Cancel obsolete probes and defer optional disruptive handoffs | Existing generation checks prevent stale commits but do not necessarily stop obsolete I/O. Cancel it promptly, retain validation, and reason-code/defer handoffs while a healthy session is observed. Measure gate wait, obsolete-probe lifetime and Engine restarts per event burst. |
| 1 | Explicit service/media evidence | HTTPS success, Gateway HELLO/ACK and desktop log evidence are different signals. Missing voice/playback evidence is unknown, not success. Keep the main screen simple; expose detail only when needed. Validate false-ready rate with real two-way voice/playback acceptance, not synthetic green checks. |
| 2 | Authenticated, version-compatible data updates with last-known-good rollback | Update reviewed rules independently from the large UI package. Start with constrained data and strict engine/schema compatibility, signatures and protected staging. Do not fetch arbitrary privileged Lua/commands. Invalid signatures/versions must fail without replacing the working set. |
| 2 | Standard-user UI and narrow privileged broker | Reduce the elevated attack surface. Design authorization, initial runtime/dependency loading, lifecycle and protected installation paths together; changing a manifest alone is insufficient. Prove unauthorized-client rejection and zero orphan processes. |
| 2 | Real compatibility evidence and safe release staging | VM/field matrix: Windows, exact driver, adapter, VPN transitions, game/filter/anti-cheat versions. Obtain authorized test environments; do not change Secure Boot, trust stores, antivirus or the current host to manufacture passes. Signatures and process-name detection are not compatibility evidence. |
| 3 | Isolated new-core experiment | Pin zapret2 and strategy revisions, retain rollback and compare with current Engine on the same acceptance matrix. Keep existing stable behavior unchanged. A fresh RC justifies evaluation, not automatic adoption. Preserve notices and resolve distribution obligations. |

Microsoft documents [native Win32/WebView2 hosting](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/win32).
It retains browser processes; removing WPF does not remove WebView2's memory.
Its [security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
requires strict web/native boundaries. Native code also introduces additional
memory/lifetime risks; a native shell is not automatically more secure.

WebView2 offers [suspension for invisible content](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.trysuspendasync).
This is a smaller prototype opportunity beyond FOG's existing paused CSS/timers:
the independent Agent must continue working, and restoring the window must
resume the renderer and replay current state. Closing FOG must still stop its
owned processes; minimized and closed are different lifecycle states.

Real per-process traffic isolation is a separate, larger design. WinDivert's
NETWORK layer does not expose processId; adding a process-name checkbox does
not create isolation. Flow/socket correlation has lifetime/race requirements.
[WinDivert layer/filter documentation](https://reqrypt.org/windivert-doc.html).

## Size Decision

Flowseal's verified official ZIP contents are only 3.87 MiB (1.10.4) and
4.46 MiB (2.0.0-rc). FOG's local uncompressed directory is 210.97 MiB, mostly
its two .NET-bearing executables. Compression yields 102.79 MiB, but increased
memory in the single measured pair. Keep it opt-in, not the default.
See [full measurements and boundaries](SIZE-STUDY-2.0.3.md).

A shared private .NET runtime is another packaging prototype, not a free fix:
the runtime is loaded before managed verification begins. Elevated external
runtime/configuration paths need an explicit trust/ACL/servicing model.
[App-relative runtime discovery](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#apphostdotnetsearch),
[native hosting sequence](https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting).
Do not silently replace portable self-contained distribution with a requirement
for users to install .NET, or promise RAM savings from smaller EXE sizes.

## Recommended Sequence

First establish repeatable full-process startup/memory and connection evidence
baselines, then implement obsolete-probe cancellation and safe renderer sleep.
In parallel, time-box the lightweight-host prototype. Choose the host/deployment
architecture from measurements before committing to migration. Next separate
privileges and introduce authenticated rule updates. Evaluate the new core on
its own track, with manual media/game acceptance before any stable promotion.

Product objective: fast, predictable recovery with little user effort. Success
metrics are session interruptions, false-ready reports, recovery distribution,
resource use and safe rollback, not preset count or claims of universal repair.
