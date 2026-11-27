# License and Distribution Checklist

The original FOG application code uses the standard MIT license in LICENSE.
Keep that text intact. Dependency terms are separate; do not claim exclusive
authorship of the Engine or driver, or that this repository contains no source.

## Included Notices

- Root THIRD_PARTY_NOTICES.md identifies the application and dependencies.
- FOG.Engine/THIRD_PARTY_NOTICES.md records Engine and packet/list provenance.
- Packages include both documents and verbatim Engine, WinDivert, .NET and
  WebView2 license/notice texts, plus a Cygwin notice. Dependency documents
  reside in `notices/`; audit and optimization reports stay in the repository,
  not in the user ZIP.
- MIT copyright and permission notices must travel with covered copies.

## 2.0.3 Verification

1. Exact Cygwin 3.4.10-1 source package and WinDivert v2.2.2 source archive are
   provided as separate release assets. Binary-to-package SHA-256 comparisons,
   source digests and build recipes are recorded in DEPENDENCY-SOURCES-2.0.3.md.
   Full matching Cygwin/newlib notices accompany the user package.
2. Corrected the earlier incomplete filename check: both pinned Flowseal
   commits provide root LICENSE.txt (MIT), although LICENSE is absent. Verified
   both immutable-revision files and retained their complete notice in the
   Engine notices. Attribution alone was not used as a license grant.
3. Retain FOG changes and packaging/build scripts with the released source.
   Official hashes reject replacement libraries. Users can rebuild the MIT
   application and regenerate the trusted runtime manifest for modified,
   interface-compatible libraries using build/package.ps1 -EngineRuntime.
4. Recheck licenses and notices whenever a binary or NuGet version changes.

This is an engineering inventory and publication checklist, not a legal
opinion or a certification of complete license compliance.

## Primary Sources

- Engine MIT: https://github.com/bol-van/zapret/blob/1a1fc38c8ea05b481eebcbd338df48cdcca23c15/docs/LICENSE.txt
- WinDivert 2.2.2 terms: https://github.com/basil00/WinDivert/blob/v2.2.2/LICENSE
- Cygwin terms and linking exception: https://www.cygwin.com/licensing.html
