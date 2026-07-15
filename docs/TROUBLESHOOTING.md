# Troubleshooting

## FOG Prime reports that components are missing

Extract the complete release ZIP. Do not move only `FOG Prime.exe`; it requires `FOG.Agent.exe`, `profiles.json`, `runtime.manifest.json`, and the `runtime` directory.

## Windows blocks startup

Check the downloaded archive properties, Windows Security history, and whether the signed WinDivert driver remains in `runtime`. Do not disable security software globally.

## Discord or YouTube check fails

Use the retry action once so Agent can run stable checks against all allowed profiles. Temporarily disable conflicting VPN or packet-filtering applications and retry. Corporate network policy may intentionally prevent this software from operating. The latest diagnostic snapshot is stored in `%ProgramData%\FOG Prime\state.json`.

## Clean removal

Close the FOG Prime window and confirm that `FOG Prime`, `FOG.Agent`, and `FOG.Engine` are no longer present in Task Manager. Then remove the extracted application folder. FOG Prime does not install a persistent Windows service.
