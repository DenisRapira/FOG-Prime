# FOG Prime 2.0.3

Release prepared after automated checks and the user's successful local test.
Publication status is recorded in release-status.json.

## Changes

- Animate the connection contours and completed stage links using CSS; show a
  success mark only for confirmed ready states. Continuous motion stops after
  success/error. No external animation dependencies or new runtime timers.
- Show three compact connection stages driven by actual Agent states, without
  profile names, invented percentages or extra timers. Respect reduced motion
  and pause stage animation while hidden.
- Suspend the hidden WebView2 renderer when minimized, retain only the latest
  status messages and resume immediately on restore. The Agent stays independent;
  browser sleep is best-effort, not a guaranteed memory reduction.
- Cancel obsolete network probes after topology changes without blocking network
  callbacks or waiting for retired checks under the supervisor gate. Preserve
  caller cancellation and reject stale results before publishing or remembering.
- Add the FOG application icon to the EXE and window (16-256 px).
- Keep the launcher at the package root and place Agent, Engine and supporting
  files under `app/`. Keep `app/` beside `FOG Prime.exe`; do not extract or move
  the EXE alone. Close the old FOG before extracting into a new directory.
- Fix Start/Stop ordering and reject stale network/VPN probe results, including
  cancellation and profile-memory paths. The managed harness now has 108 tests.
- Keep user ZIPs minimal; development reports and screenshots are not bundled.
  Required dependency license/source notices remain in `notices/`.
- Check source/site versions, mocked UI states and managed advisories in CI.
  Tagged builds create drafts for exact-artifact manual acceptance, not
  automatically published stable releases.
- Lower background load through adaptive health/UI polling, event-driven
  reconciliation, cached snapshots and fewer redundant state writes.
- Remember working profiles independently for up to 16 network/mode contexts.
  Game Filter remains manual; opening Discord or a game does not launch FOG.
- See OPTIMIZATION-2.0.3.md in source documentation for measured idle results
  and deferred independent-strategy/signed-rule-update functionality.
- Application and build toolchain move to .NET 10 LTS with locked restores.
- Agent retains verified runtime/manifest file locks and ancestor-directory
  locks until shutdown, preventing file replacement and directory-renaming
  races in normal Windows file operations. Repeated checks reuse the lease.
- UI pins the verified Agent through launch and authentication. Reparse-point
  runtime/package paths are rejected; extract into a regular local directory.
- IPC reads/writes and GitHub update responses have bounded deadlines; update
  responses are limited to 128 KiB and redirects are not followed.
- Consolidated dependency notices include the pinned Flowseal MIT license and
  matching Cygwin/newlib terms. Exact Cygwin/WinDivert source archives accompany
  the release separately; binary package hashes were verified. See
  DEPENDENCY-SOURCES-2.0.3.md and LICENSE-COMPLIANCE.md.
- One Game Filter switch for the current game catalog: ROB RUN and Warframe.
- The game catalog stays internal; no game list is displayed in the interface.
- Enabled game-domain rules use the embedded catalog. Warframe adds
  `warframe.com` to the existing general service host lists, covering its
  subdomains under the Engine's existing host-list matching semantics.
- No change to the original Discord/YouTube rule priority, four-profile search,
  signed runtime driver or upstream packet/IP-set assets.
- Game Filter remains off by default, with the existing choice preserved.
  Switching modes reconnects FOG, not the game. A healthy external VPN retains
  direct mode, without changes to VPN configuration.

## Scope

The TCP/UDP 1024-65535 filter was already destination-based, not ROB RUN process
filtering. It can match other games on included networks. Adding a game to the
catalog does not include all its servers, guarantee voice/chat/gameplay, or
repair a server-side battle session. Peer-to-peer destinations outside the
pinned IP set are not automatically included. Other apps on the same CDN or
hosting networks may also match. No arbitrary new IP ranges are added.

Warframe uses UDP 4950 and 4955 by default, within the existing game port range:
https://www.warframe.com/en/portinuse
Its official support also documents non-filtering causes of chat failures:
https://support.warframe.com/hc/en-us/articles/360027199352-Common-chat-issues-error-10053-10054-spinning-circle-etc
These sources do not establish that DPI filtering repairs NAT or every chat issue.

## Acceptance

Automated policy, catalog, UI and native parameter checks must pass before
manual acceptance. Live game/provider testing is still required without VPN;
no live game or driver-loading tests are claimed by these offline checks.
This build does not install certificates, reset DNS/adapters, restart games,
modify anti-cheat settings or disable an external VPN.
