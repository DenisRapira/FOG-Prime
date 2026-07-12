# Troubleshooting

## FOG Prime reports that components are missing

Extract the complete release ZIP. Do not move only `FOG Prime.exe`; it requires `FOG.Agent.exe`, `profiles.json`, `runtime.manifest.json`, and the `runtime` directory.

## Windows blocks startup

Check the downloaded archive properties, Windows Security history, and whether the signed WinDivert driver remains in `runtime`. Do not disable security software globally.

## Discord check fails

Restart FOG Prime so Agent can retry all allowed profiles. Temporarily disable conflicting VPN or packet-filtering applications and retry. Corporate network policy may intentionally prevent this software from operating.

## Clean removal

Run `build/uninstall-agent-service.ps1` from an elevated PowerShell session if Agent was installed as a service, then remove the extracted application folder.
