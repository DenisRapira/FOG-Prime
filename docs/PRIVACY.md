# Privacy

FOG Prime is designed to operate locally.

- It does not require a FOG-operated account or remote control server.
- It does not intentionally collect analytics, identifiers, browsing history, message content, or Discord credentials.
- Health checks contact public Discord and YouTube endpoints.
- Optional desktop connection evidence reads up to 64 KiB of the local stable
  Discord renderer log. Only recent Gateway/voice connection states are used;
  raw log contents are not copied to FOG diagnostics or sent to a server.
- The UI checks GitHub's public Releases API for a newer FOG Prime version and
  opens the official release page only after the user chooses to download it.
- The latest Agent state is stored locally in `%LOCALAPPDATA%\FOG Prime\state.json`.
- Bounded diagnostics for the current and previous launch are stored locally in
  `%LOCALAPPDATA%\FOG Prime\logs`; session tokens are not written to these files.
- Windows, WebView2, Discord, GitHub, antivirus products, and network providers have their own independent privacy practices.

Do not attach unredacted state files, logs, screenshots, private domains, IP addresses, or tokens to public issues.
