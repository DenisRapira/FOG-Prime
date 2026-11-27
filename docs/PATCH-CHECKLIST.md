# Patch and Release Acceptance

This checklist is a release gate, not a claim that all defects can be excluded.
The tag workflow creates a **draft only**. A maintainer must review the exact
downloadable artifact before publishing it. A locally tested package is not
evidence that a newly rebuilt CI package is identical or behaves identically.

## Before Building

- Keep `docs/release-status.json` truthful: `stable` is the currently public
  release, `candidate` is the version being prepared. The stable framework can
  differ from the candidate framework. Do not advance stable on a tag alone.
- Update the project version, embedded UI, release notes, CHANGELOG, both site
  translations, README and support/build documentation. Run
  `build/test-release-consistency.ps1` and the UI/site tests.
- Review the dependency changes, locked restore, upstream source revision,
  local native patches, payload origins and exact license/source obligations.
  Preserve third-party notices. Resolve the pending items in
  `LICENSE-COMPLIANCE.md` before distributing affected new artifacts.
- Review all tracked changes and ensure keys, PFX files, private certificates,
  tokens, real user diagnostics, build directories and unrelated files are not
  committed. `git diff --check` is necessary but is not a secret scanner.

## Automated Evidence

- Clean locked restore and Release build with the SDK from `global.json`.
- Run Agent tests and repository verification. Source-pattern checks are
  regression guards, not proof of runtime security or network connectivity.
- Run package security tests, native profile `--dry-run` checks, UI mocked-state
  tests and bilingual website checks. Never silently substitute a dry run for
  a driver, game or two-way voice test.
- Inventory every distributed EXE/DLL/SYS: SHA-256, Authenticode signer/status,
  source origin and matching build manifest. An unsigned application must not
  be described as Microsoft-signed because its bundled driver is signed.
- Record the actual antivirus result and engine/date, or explicitly state
  that no scan ran. VirusTotal hash lookup is not file analysis and an absent
  report is not a clean result. Upload nothing without separate consent.

## Manual Acceptance of the Exact ZIP

Record ZIP hash, source commit, dirty-tree status, Windows version, network
conditions and results; redact personal addresses and Discord identifiers.
At minimum check fresh extraction/start, first successful connection, repeated
start/stop, closing all owned processes, remembered profiles and fallback,
manual Game Filter, Discord messages and two-way voice, sustained YouTube,
game login/chat, network change and recovery, VPN present/removed and sleep.
Check representative anti-cheat compatibility without bypassing anti-cheat.
Missing VM/provider/game access means those scenarios remain unverified.

The unsigned native-driver development path needs isolated VM testing and
production signing separately; do not enable test signing or install trust
certificates on users' machines as a release workaround.

## Publish and Roll Back

- Download the draft ZIP and checksum again and verify they match the accepted
  artifact. Archive the acceptance record with that immutable hash.
- Publish only after unresolved distribution/security blockers are resolved.
  Include limitations, upgrade instructions, known issues, driver/application
  signature status and rollback instructions in the release description.
- Retain previous stable assets and tags. Do not replace an existing release
  ZIP silently: create a new version for every byte-changing fix.
- Only after publication, verify GitHub's latest-release API and asset URLs,
  update stable metadata and both site pages, then test the deployed Pages
  URL. The app offers the release page; it does not silently install updates.
- For regressions, stop recommending the bad patch, link the last verified
  stable release and document which users are affected. Do not reset users'
  network configuration or delete settings as a blanket rollback.

As of 2026-11-27 the public v2.0.2 release has a ZIP but no separate SHA-256
asset. Do not describe that missing asset as available. A maintainer can add
the checksum only after independently hashing the already published ZIP;
the audit does not upload it or modify the existing release.
