# Third-party notices

The root MIT LICENSE applies to original FOG Prime code, Copyright (c) 2026
DenisRapira. It does not replace licenses or copyright notices of dependencies.
Third-party names do not imply endorsement.

## Engine

FOG.Engine.exe is derived from bol-van/zapret (MIT), with FOG-specific changes.
This repository includes the pinned upstream source under FOG.Engine/upstream;
it is not a binary-only repository. The original notice is Copyright (c)
2016-2024 bol-van. Release packages retain it in notices/Engine-MIT-LICENSE.txt.
Revision and modifications: FOG.Engine/UPSTREAM.md in source, notices/UPSTREAM.md in packages.
Source: https://github.com/bol-van/zapret/tree/1a1fc38c8ea05b481eebcbd338df48cdcca23c15

## WinDivert 2.2.2

WinDivert.dll and WinDivert64.sys are distributed as separate, unmodified
runtime files. Upstream offers LGPL version 3 or GPL version 2; FOG uses the
LGPLv3 option. Full upstream terms, including LGPLv3 and GPLv3, are retained
in notices/WinDivert-LICENSE.txt. Do not remove original notices.
Source and build files: https://github.com/basil00/WinDivert/tree/v2.2.2
Matching source archive and binary-hash comparison: docs/DEPENDENCY-SOURCES-2.0.3.md.
The source archive is a separate asset on the v2.0.3 release page.

## Cygwin

The working runtime includes cygwin1.dll, identified by its file metadata as
version 3.4.10, Copyright Cygwin Authors 1996-2023. Cygwin's API library uses
LGPLv3-or-later with the Cygwin linking exception. See notices/Cygwin-NOTICE.txt
and the included LGPLv3/GPLv3 texts in notices/WinDivert-LICENSE.txt.
Licensing and source access: https://www.cygwin.com/licensing.html
https://www.cygwin.com/git.html
The matching 3.4.10-1 source package, including its build recipe, is a separate
v2.0.3 release asset; see docs/DEPENDENCY-SOURCES-2.0.3.md. The DLL was verified
byte-for-byte against the official-version binary package. Complete matching
Cygwin/newlib license notices are retained in notices/Cygwin-NOTICE.txt.

## Microsoft components

Self-contained application builds include the .NET runtime and Microsoft
Extensions packages. Their MIT license and third-party notices are retained in
notices/DotNet-LICENSE.txt and notices/DotNet-ThirdPartyNotices.txt.
Source: https://github.com/dotnet/dotnet

The WebView2 SDK and loader use Microsoft's applicable SDK terms. Their
original texts are included as notices/WebView2-LICENSE.txt and
notices/WebView2-NOTICE.txt. The installed Edge WebView2 Runtime has its own
terms; it is not included in this portable package.
https://www.nuget.org/packages/Microsoft.Web.WebView2/

## Packet samples and network lists

The build also includes referenced packet samples and lists. Preserve their
provenance; see FOG.Engine/THIRD_PARTY_NOTICES.md in source and notices/ENGINE-NOTICES.md
in release packages. Both pinned Flowseal revisions include MIT LICENSE.txt;
its complete text is retained in notices/ENGINE-NOTICES.md. Dependency terms
remain separate. See docs/LICENSE-COMPLIANCE.md for the verification record;
these notices are not a legal certification or an endorsement.

## Modification and source access

The integrity checks in official binaries accept only their build-time hashes.
To use modified, interface-compatible LGPL libraries, build FOG from source and
run build/package.ps1 with -EngineRuntime pointing to the modified runtime.
Packaging embeds a new trusted manifest. Retain license texts and provide the
corresponding source and build instructions for the exact binaries distributed.
This notice does not restrict modification or reverse engineering for debugging
changes to LGPL-covered components. See docs/BUILDING.md and the component terms.
