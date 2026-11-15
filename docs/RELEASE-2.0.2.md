# FOG Prime 2.0.2 - Working-Runtime Distribution

## Главное

- Откройте FOG: подключение запускается автоматически, выбирать профиль не нужно.
- Вместо технических этапов отображается один понятный статус.
- Краткий сбой не вызывает тревожное сообщение; при длительном сбое доступна
  кнопка «Повторить», которая перезапускает подключение FOG, а не игру.
- При закрытии окна останавливаются процессы FOG. VPN не отключается.
- Сохранены уведомление о VPN и предложение скачать новую версию с GitHub.
- Добавлен переключатель «Игровой фильтр», первая игра в списке - ROB RUN.
  По умолчанию выключен; выбор сохраняется. Включение применяет правила из
  1.10.4: TCP/UDP 1024-65535 по закреплённому IP-списку, с исключением локальных
  сетей и приоритетом Discord/YouTube. Игра автоматически не перезапускается.
- Рабочий движок и оригинальный подписанный драйвер сохранены. Тестового
  native-драйвера и установки сертификатов в этой сборке нет.

Скачайте ZIP, распакуйте его целиком и запустите `FOG Prime.exe`.
Автоматические проверки не заменяют проверку звука Discord и воспроизведения
YouTube на конкретном провайдере; такие живые тесты этой сборки не выполнялись.

Release 2.0.2. Automated checks cover local lifecycle, integrity and interface
behavior. Live application/provider acceptance of this exact build was not run.

Extract the complete package and run `FOG Prime.exe`, accepting Windows
administrator elevation. Keep the window open while FOG is needed. Do not
launch individual files from `runtime` or mix files from different builds.

## Changes

- Automatic connection on launch, with one calm status instead of a technical
  checklist, profile names or diagnostic output.
- Short outages remain in background recovery; a sustained failure exposes
  one Retry action. Retry restarts FOG's connection cycle, not the game.
- Readiness requires confirmed backend health and an available Engine or
  external tunnel. VPN presence alone never means a working connection.
- Preserve the VPN warning and optional Download/Later update prompt. Update
  download opens the official release page; no silent installer is claimed.

- Preserve qualified live Engine sessions during persistent service-probe
  failures, without deleting the remembered profile. Initial bounded profile
  qualification remains enabled; actual Engine exits still use supervisor
  recovery rather than being reported as a healthy direct connection.
- Reject cached Engine-assisted success as direct VPN readiness, including
  results arriving after Engine exit.
- Honor cancellation in DNS probing and before applying health results.
- Add optional application signing in dependency order: Engine, runtime
  manifest, Agent, embedded Agent hash, UI, final package checksums.
- Do not close or restart ROB RUN, modify its files, surrender a character,
  reset network adapters, or change an external VPN.

## Signing And Runtime

This distribution follows the established 2.0.1 signing model: the FOG EXEs are
unsigned, and the working runtime driver retains its original valid signature.
Windows may report an unknown application publisher or display SmartScreen
warnings. No third-party private key is used and no signature is transferred
to a modified file. See build-info.json for the exact driver identity and hash.
No TEST ONLY application or native-driver signature is used in this package.

The working runtime uses the existing WinDivert driver and third-party notices.
Packaging verifies Windows trust and preserves the driver's bytes. Kernel-policy
signature verification is a separate check, not proof of anti-cheat compatibility.
The independent FOG.Native.sys driver remains outside this distribution.
No certificate store, boot security, system DNS, or external VPN is changed
while building or verifying this distribution.

## Validation Boundary

Offline tests do not establish live Discord voice, YouTube playback, anti-cheat
compatibility, or recovery of ROB RUN's backend session. Existing service rules
retain priority. Capture expands to TCP/UDP 1024-65535 for the appended Game
Filter rules when the user enables the switch (default off), scoped to a pinned upstream IP set, excluding local networks,
with processing limited by n4 cutoff. This is destination-based, not per-game
process filtering: other applications using listed CDN/hosting networks may
also be affected. Game compatibility and lag recovery are not guaranteed.
The switch choice is persisted. Turning it off restores the previous narrow
capture and service rules. A healthy external VPN keeps direct mode; the choice
is applied when Engine is used, not by changing the VPN.

If a qualified Engine stays alive but connectivity is persistently degraded,
this version preserves it instead of automatically cycling profiles. This
avoids disrupting established connections but may require an explicit restart
of FOG after an actual network restriction changes.

Manual acceptance remains required on the intended Windows/network setups.
Do not publish this candidate as a guaranteed ROB RUN lag fix or as an entirely
independent production-signed kernel engine.
