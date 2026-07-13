# FOG Engine Upstream

FOG Engine is built from the `nfq/winws` Windows target in the upstream source tree.

| Component | Pinned revision | License |
| --- | --- | --- |
| bol-van/zapret | `1a1fc38c8ea05b481eebcbd338df48cdcca23c15` | MIT |
| bol-van/zapret-win-bundle | `0e9e3fbfb04a1681f3f8b5eb644dee4ecedcccf0` | MIT |
| WinDivert | 2.2.2 | LGPL-3.0-or-later or GPL-2.0-only |

The copied upstream source is retained in `upstream/` for reproducible builds. Product code must retain the upstream notices and must not claim authorship of the third-party engine or driver.

## FOG patch set

- Synchronize the SSRC in generated Discord Voice IP Discovery packets with the
  current client request. This prevents a server response to a static fake SSRC
  from being rejected by Discord as `NO_ROUTE`.
- Restrict dynamic SSRC synchronization to payloads that are actually Discord
  Voice IP Discovery requests. Non-Discord QUIC decoys must remain byte-exact.
