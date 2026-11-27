# Third-party notices

## Flowseal strategy reference and packet payloads

The FOG `fog-connect-1102` profile adapts the target-service rules from
Flowseal/zapret-discord-youtube release 1.10.2, commit
`dfd8e613b099676cf2aa7b474ee5923801514dec`.
The three non-executable packet samples in `payloads/flowseal-1.10.2` retain
their upstream filenames. Their provenance and SHA-256 digests are recorded in
`manifest.json` and distributed as `payload-provenance.json`.
Source: https://github.com/Flowseal/zapret-discord-youtube/tree/1.10.2/bin
The pinned revision includes LICENSE.txt under MIT; its complete notice is
retained below. The same license text was verified at both referenced revisions.
No Flowseal batch scripts, updater, executable or hosts file are executed or
installed by this integration.

The background Game Filter adapts the final TCP/UDP rules of release 1.10.4
(`general (ALT11).bat`), commit `176f56dc3f11ce5915c26abcdde903f93d835b64`.
Two packet samples and the upstream service IP set/local exclusions are pinned
in `payloads/flowseal-1.10.4/manifest.json`, distributed as
`game-filter-provenance.json`. The include list originates from
`.service/ipset-service.txt`, not the placeholder `lists/ipset-all.txt`.
This list includes broad CDN/hosting networks; filtering is destination-based,
not game-process-based. Empty lists and default routes are rejected at build time.
Source: https://github.com/Flowseal/zapret-discord-youtube/tree/1.10.4
The pinned MIT LICENSE.txt covers the repository materials; separate runtime
dependencies retain their own terms as described below and in the root notices.

## zapret

Copyright (c) 2016-2024 bol-van (as retained in the pinned upstream license).

FOG Engine is derived from the `nfq/winws` Windows target of `bol-van/zapret` and is distributed under the MIT License. The upstream MIT license is retained in `upstream/docs/LICENSE.txt`.

## WinDivert

Copyright (c) 2015-2022 basil.

FOG Engine uses WinDivert as a separate Windows packet-diversion dependency.
Upstream offers LGPL version 3 or GPL version 2; FOG uses the LGPLv3 option.
Releases retain the upstream license bundle and notices. See the root
THIRD_PARTY_NOTICES.md for Cygwin, Microsoft components and source obligations.

## Flowseal LICENSE.txt (pinned revisions, trailing whitespace normalized)

MIT License

Copyright (c) 2016-2026 bol-van
Copyright (c) 2024-2026 Flowseal

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

This repository contains binary files originating from the project by bol-van,
available at: https://github.com/bol-van/zapret/ (licensed under the MIT License).

This repository also includes and depends on WinDivert
(https://github.com/basil00/WinDivert), which is licensed under your choice of:

1. The GNU Lesser General Public License (LGPL) Version 3, or
2. The GNU General Public License (GPL) Version 2.

Binary distributions of WinDivert are included in this project as-is, without modification.
The corresponding source code and license terms for WinDivert are available at
https://github.com/basil00/WinDivert.

---

To comply with the licenses of these projects:

1. The original copyright notices and licenses (above) are retained.
2. The use of WinDivert in this project is governed by its licensing terms (LGPLv3/GPLv2).
3. This repository provides only binary files and does not include the source code of
   the project by bol-van or modifications to WinDivert.
