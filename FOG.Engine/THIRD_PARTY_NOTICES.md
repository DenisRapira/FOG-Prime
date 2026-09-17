# Third-party notices

## Flowseal strategy reference and packet payloads

The FOG `fog-connect-1102` profile adapts the target-service rules from
Flowseal/zapret-discord-youtube release 1.10.2, commit
`dfd8e613b099676cf2aa7b474ee5923801514dec`.
The three non-executable packet samples in `payloads/flowseal-1.10.2` retain
their upstream filenames. Their provenance and SHA-256 digests are recorded in
`manifest.json` and distributed as `payload-provenance.json`.
Source: https://github.com/Flowseal/zapret-discord-youtube/tree/1.10.2/bin
The upstream repository does not provide a root LICENSE file at this tag;
this attribution does not assert a separate license grant from Flowseal.
No Flowseal batch scripts, updater, executable, broad IP sets or hosts file
are executed or installed by this integration.

## zapret

Copyright (c) 2016-2026 bol-van.

FOG Engine is derived from the `nfq/winws` Windows target of `bol-van/zapret` and is distributed under the MIT License. The upstream MIT license is retained in `upstream/docs/LICENSE.txt`.

## WinDivert

Copyright (c) 2015-2022 basil.

FOG Engine uses WinDivert as a separate Windows packet-diversion dependency. WinDivert is available under LGPL-3.0-or-later or GPL-2.0-only. Releases must include the selected license and corresponding notice.
