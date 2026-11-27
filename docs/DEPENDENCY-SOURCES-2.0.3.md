# Native Dependency Sources for 2.0.3

The v2.0.3 release provides source archives separately from the user ZIP:
https://github.com/DenisRapira/FOG-Prime/releases/tag/v2.0.3
They are not executable installers and are not needed to run FOG.

## Cygwin 3.4.10-1

- Source asset: `cygwin-3.4.10-1-src.tar.xz`.
- SHA-256: `AC70AF0D4E644732F74946F55F7BAFE010BAB9B9DA39A9327C3F0367F1FA43A2`.
- Downloaded over HTTPS from the Cygwin package mirror:
  https://ftp.cvut.cz/mirrors/cygwin.com/x86_64/release/cygwin/cygwin-3.4.10-1-src.tar.xz
- Matching binary package SHA-256:
  `BB81511484DE5E0182B0BAEFAD1BD1ABEE618FA0656A25B283B0A32FF72AC088`.
- Binary origin:
  https://ftp.cvut.cz/mirrors/cygwin.com/x86_64/release/cygwin/cygwin-3.4.10-1.tar.xz
- The package's `usr/bin/cygwin1.dll` matches the distributed DLL byte-for-byte:
  `103104A52E5293CE418944725DF19E2BF81AD9269B9A120D71D39028E821499B`.

The source package contains `cygwin.cygport` and
`newlib-cygwin-3.4.10.tar.bz2`, including Cygwin and newlib source/license files.
The recipe pins revision `cygwin-3.4.10-0-g89a1e4a573d4` and lists BUILD_REQUIRES.
On an x86_64 Cygwin build environment install those declared build dependencies
and cygport, extract the outer archive, then run
`cygport cygwin.cygport all` in its `cygwin-3.4.10-1.src` directory.
This release pass inspected the recipe and binary match; it did not rebuild the
library or promise a byte-identical locally rebuilt DLL.

## WinDivert 2.2.2

- Source asset: `WinDivert-2.2.2-source.tar.gz`.
- Revision: `1789526ecfb9ff5397c94f9f54c1a3dc2fb60440` (upstream v2.2.2).
- SHA-256: `B26A67DD9F58A899DB1AA5B47A467C0EFB98CF0D6CF357EC46C1198C260CCD7F`.
- Source origin:
  https://api.github.com/repos/basil00/WinDivert/tarball/1789526ecfb9ff5397c94f9f54c1a3dc2fb60440
- Official binary origin:
  https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip
- Both distributed x64 files match entries in that pinned upstream ZIP exactly:
  DLL `C1E060EE19444A259B2162F8AF0F3FE8C4428A1C6F694DCE20DE194AC8D7D9A2`;
  SYS `8DA085332782708D8767BCACE5327A6EC7283C17CFB85E40B03CD2323A90DDC2`.

The source includes README, `msvc-build.bat`, `wddk-build.bat`,
`mingw-build.sh`, `release-build.sh` and driver Makefiles. Follow the included
README/toolchain requirements. A rebuilt kernel driver needs suitable Windows
signing to load normally; source availability does not provide a production
certificate. FOG uses the LGPLv3 distribution option and retains full terms.

## Engine and Referenced Assets

FOG Engine source, local changes and build scripts are in `FOG.Engine/` in the
tagged FOG repository. Flowseal LICENSE.txt was checked at both pinned commits:
`dfd8e613b099676cf2aa7b474ee5923801514dec` and
`176f56dc3f11ce5915c26abcdde903f93d835b64`.
Both original license files have SHA-256
`FE3983A1E91206AD1A530BCFAE01FAD207020CB61882EDD62C1E3CB5F8D5D430`.
Their MIT notice is retained in `FOG.Engine/THIRD_PARTY_NOTICES.md` and in the
package's `notices/ENGINE-NOTICES.md`; other components keep their own terms.

Users may rebuild interface-compatible dependencies and regenerate the FOG
trusted manifest with `build/package.ps1 -EngineRuntime`; see BUILDING.md.
This is a verified engineering inventory, not a legal opinion. Recheck the
archive mapping and terms when updating any dependency.
