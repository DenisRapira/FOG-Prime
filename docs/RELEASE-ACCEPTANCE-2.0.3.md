# 2.0.3 Release Acceptance

The user reported successful operation of the motion-candidate.2 package and
authorized publication. No provider/game/anti-cheat matrix was supplied, so
this is a local user acceptance, not universal compatibility certification.

The published package reuses that tested candidate's executable, library,
driver, profile and packet/list bytes without rebuilding them. A clean rebuild
was made during release preparation but changed executable hashes; it is not
silently substituted for the user-tested binaries. Only dependency notice
documents and generated packaging/provenance metadata are updated for release.

The original candidate's build-info `sourceCommit` and `workingTreeModified`
remain intact: it was built from a modified working tree based on
`c8f23bf7b018133d43cf5e576033c5c2d27749ef`. That application source was captured
in commit `3d26554` before publication. The tag also includes release notice
and documentation corrections. `releaseSourceCommit` identifies the final
tagged snapshot; it does not relabel the original build as a clean rebuild.

Release assembly compares every non-notice file with the accepted candidate,
recalculates the package hash inventory and verifies exact original binary
reuse. The ZIP has a separate SHA-256 asset. Exact dependency sources are
separate release assets, not bundled with the user application.

Automated evidence: 108/108 managed tests; seven mocked UI states at three
sizes, motion/reduced-motion/visibility/layout checks; package tamper/IPC tests;
eight native parameter dry runs; real WebView2 minimize/restore/close with
native Engine autostart disabled. These checks do not prove voice, gameplay,
provider behavior or absence of all leaks/security defects.

Application EXEs are unsigned. The retained original driver has a valid
Windows signature. No VirusTotal upload or clean antivirus verdict is claimed.
No host VPN, DNS, routes or certificates were changed during release assembly.
