# Support

Use [GitHub Issues](https://github.com/DenisRapira/FOG-Prime/issues) for reproducible application bugs and focused feature requests.

Before opening an issue:

1. Identify the version and source: published stable 2.0.2/.NET 8 or unpublished candidate 2.0.3/.NET 10. Use the [official Releases page](https://github.com/DenisRapira/FOG-Prime/releases/latest) for downloads.
2. Close and reopen FOG Prime; record whether the problem concerns startup, probes or an actual application session.
3. Confirm WebView2 Runtime and Base Filtering Engine (`BFE`) are available; record any active VPN and the manual game-filter setting.
4. Remove secrets and personal network information from screenshots and logs.

Include Windows version, exact error, reproduction steps, and a small redacted excerpt from `%LOCALAPPDATA%\FOG Prime\logs` or `state.json`. These can contain paths, adapter names, domains and IP information. Do not post raw Discord renderer logs. See [Privacy](docs/PRIVACY.md) and [Troubleshooting](docs/TROUBLESHOOTING.md).

Security vulnerabilities must be reported privately according to [SECURITY.md](SECURITY.md).

The project cannot guarantee compatibility with every provider, VPN, antivirus, corporate firewall, anti-cheat, or modified Windows installation.

A green synthetic probe or recent Discord `RTC_CONNECTED` entry does not prove audible voice, video playback or working NAT. The manual game filter does not start from Discord/game launch and does not provide per-process isolation. No verified antivirus verdict is available from this audit; do not disable protection or upload files to a scanner without authorization.
