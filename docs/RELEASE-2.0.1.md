# FOG Prime 2.0.1

Address-list update on the existing 2.0.0 engine and UI.

- Added robrun.com, kefirgames.com, robrun.wiki, robrun.site, robrun.org,
  epicgames.com, epicgames.dev and easyanticheat.net to existing TCP/UDP hostlists.
- Existing capture ports, packet strategies, automatic profile order and UI
  are unchanged. Discord and YouTube profiles are retained.
- Uses the existing third-party signed driver. The experimental unsigned
  FOG Native driver and independent engine are NOT included.
- Updated the displayed version and download documentation to 2.0.1.
- Third-party copyright, license and provenance notices are retained.

## Checks

48/48 compatibility tests passed. Package integrity and the existing driver's
signature were checked. No real provider/game/voice tests without VPN were
performed for these address-list changes. Domain coverage alone does not
guarantee game chat, voice, authentication or anti-cheat compatibility.
No anti-cheat protection, VPN setting, firewall or system DNS is disabled.

## Installation

Download FOG-Prime-2.0.1-win-x64.zip, extract the entire archive, then run
FOG Prime.exe. Do not replace individual files inside an older installation.
Keep all runtime and notice files together. The archive SHA-256 is supplied
as FOG-Prime-2.0.1-win-x64.sha256.

If this version behaves differently on your network, the previous v2.0.0
release remains available. Do not use the FOG Native development artifacts as
an alternative public release.
