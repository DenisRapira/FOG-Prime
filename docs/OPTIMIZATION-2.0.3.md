# FOG Prime 2.0.3 - Background Work and Network Memory

Performance baseline: 2026-11-25. Updated verification: 2026-11-27. Local candidate; no stable publication yet.

This report and its measurements remain in the repository, not in the minimal candidate ZIP. The measurements below describe the named builds, not a later repackaged artifact; removing unused package files does not establish a runtime performance improvement.

## Implemented

- Stable Engine health probes: 60 seconds instead of 20. Direct VPN mode:
  15 seconds instead of 5. Startup/recovery remains on a three-second schedule;
  degraded mode uses ten seconds. Stopped recovery performs no network probes.
- Compatibility reconciliation: 15 seconds when stable, three when recovering,
  two minutes when stopped, with event-driven wakeups on network changes and
  Engine exit. Event bursts coalesce and receive a 750 ms settling delay.
- Network adapter snapshots cache for at most five seconds and invalidate on
  network events. Process-name snapshots cache for at most 15 seconds.
- Changed network identity invalidates previous probe evidence and in-memory
  profile selection. An established Engine is not restarted solely for an
  address-change notification. Fresh health checks are scheduled instead.
- Up to 16 network/profile-catalog/game-mode contexts are remembered for seven
  days. Switching A -> B -> A does not discard A. A failed preference is removed
  only from its context. Valid old v2 state migrates to the bounded v3 history.
- UI status polling: one second during transitions, three seconds when stable,
  ten seconds when minimized. Each actual request authenticates the Agent;
  the extra pre-request ping is removed during a healthy session. Three failed
  requests still enable the existing bounded Agent recovery path.
- Unchanged UI payloads do not cross WebView again. Hidden clocks/ambient
  animation pause. Ready events after document recreation force a state resend.
- State is persisted on semantic changes and at two-minute checkpoints, not
  every health cycle. Game-filter state and per-probe success changes count.
- Game Filter remains manual, as requested. There is no Discord/game-triggered
  launch of FOG, no game restart and no change to VPN/DNS/network configuration.

Race fixes verified on 2026-11-27 serialize Start/Stop running intent under the
supervisor gate and leave it unchanged for cancelled waiters. Network probes
and cached successes carry network identity/topology-generation context;
stale results are discarded and fresh checks scheduled without replacing an
established Engine solely for the topology change. Cancelled probes do not
publish results. This does not establish live VPN handoff correctness or a
new performance improvement.

## Short Idle Measurement

Same host and existing VPN configuration, no-autostart mode, five-second warmup
and approximately 20 seconds sampled per package using build/measure-idle-load.ps1.
One sample per build; this is not a statistical benchmark. Other developer/test
work was running on the host. Native Engine and WebView2 child processes are
excluded, so these numbers do not measure in-game packet processing or the
whole application process tree.

| Metric | Security candidate .2 | Optimized candidate |
| --- | ---: | ---: |
| UI CPU time over 20 seconds | 468.750 ms | 156.250 ms |
| Agent CPU time over 20 seconds | 703.125 ms | 109.375 ms |
| UI working set | 115.16 MiB | 118.74 MiB |
| Agent working set | 65.96 MiB | 57.99 MiB |
| UI private memory | 70.01 MiB | 71.74 MiB |
| Agent private memory | 25.86 MiB | 20.12 MiB |

Observed combined UI/Agent CPU time fell from about 1.17 s to 0.27 s in this
sample. UI memory did not fall. No universal speedup, leak-free operation,
native-Engine reduction or long-session result is claimed.

## Verification and Limits

The original 2026-11-25 baseline passed 89 compatibility tests, including cache collection counts, cancellation,
scheduler bounds, coalesced notifications, stopped-mode behavior, preservation
of a dedicated test child across network changes, and multi-network memory.
The child is not the actual Engine and does not load a driver. Package security,
eight native parameter dry-runs and UI tests at three window sizes also passed
for those recorded candidates.

Latest verification, 2026-11-27: Release build with zero warnings/errors and
97/97 scoped tests passed, including eight additional race-regression entries.
These results do not remeasure the baseline or verify a newly packaged minimal
ZIP. Earlier candidate hashes remain historical artifact identifiers.

Actual Wi-Fi/VPN/sleep transitions and active game sessions remain manual tests;
the current VPN was not toggled. A network event is a prompt to check, not proof
of successful restoration. Missed notifications retain a periodic fallback.

## Deferred Functionality

Independent live strategy replacement for Discord/YouTube/games is not included:
the existing native backend uses one combined filtering process. Starting
multiple competing filter processes is not an implementation of isolation.
Separate signed rule updates also remain pending a publisher-controlled signing
key and a release/rollback channel. This build does not download executable
rules or trust an unsigned remote game catalog. Automatic game detection is
not enabled; the user explicitly retained the manual switch.
