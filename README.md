<div align="center">
  <img src="docs/assets/fog-prime-social-preview.png" alt="FOG Prime - Windows, .NET, Networking" width="100%" />

  # FOG Prime

  **Educational Windows network diagnostics with a local Agent/Engine architecture. Published 2.0.2: .NET 8; unpublished 2.0.3 candidate: .NET 10.**

  [![CI](https://github.com/DenisRapira/FOG-Prime/actions/workflows/ci.yml/badge.svg)](https://github.com/DenisRapira/FOG-Prime/actions/workflows/ci.yml)
  [![CodeQL](https://github.com/DenisRapira/FOG-Prime/actions/workflows/codeql.yml/badge.svg)](https://github.com/DenisRapira/FOG-Prime/actions/workflows/codeql.yml)
  [![Release](https://img.shields.io/github/v/release/DenisRapira/FOG-Prime?display_name=tag&sort=semver)](https://github.com/DenisRapira/FOG-Prime/releases/latest)
  [![Downloads](https://img.shields.io/github/downloads/DenisRapira/FOG-Prime/total)](https://github.com/DenisRapira/FOG-Prime/releases)
  [![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows11&logoColor=white)](#system-requirements)
  [![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
  [![License](https://img.shields.io/github/license/DenisRapira/FOG-Prime)](LICENSE)
  [![Privacy](https://img.shields.io/badge/Privacy-local_diagnostics-F50DB4)](#security-and-privacy)
</div>

**FOG Prime** is an open-source educational Windows application for exploring network diagnostics, automatic connection profiles, and local process supervision. Its current checks cover DNS, certificate-validated HTTPS and a synthetic Discord Gateway WebSocket session. Engine profiles also contain QUIC/UDP rules; those rules are not an end-to-end QUIC or voice test. No FOG-operated backend is required, but probes and update checks contact third parties; see [Privacy](docs/PRIVACY.md).

**FOG Prime** - образовательное open-source приложение для Windows 10/11, демонстрирующее сетевую диагностику, автоматический выбор профиля и доверенную локальную архитектуру UI / Agent / Engine. Подключение запускается автоматически, интерфейс показывает один понятный статус.

> [!IMPORTANT]
> FOG Prime is a local networking utility and reference implementation. It does not provide a hosted VPN, proxy endpoint, account, subscription, anonymity guarantee, or endpoint-security product.

## Download

### 2.0.3 In Development

This working tree targets .NET 10 (`global.json` selects SDK 10.0.404). It is not the published 2.0.2/.NET 8 package. The .NET 10 badge and source build instructions describe the candidate, not the stable download. Canonical status is in [release-status.json](docs/release-status.json). GitHub's live API was verified on 2026-11-27: stable v2.0.2 was published at `2026-11-15T11:47:08Z`, with only `FOG-Prime-2.0.2-win-x64.zip` (100386176 bytes); no `.sha256` asset is published.

Готовится локальная тестовая сборка с общим каталогом игр **ROB RUN и Warframe**.
Каталог работает внутри приложения; в окне остаётся только переключатель фильтра.
Добавлены снижение фоновых опросов, реакция на события сети и отдельная память
рабочих профилей для разных сетей. Автозапуска при открытии Discord или игры нет.
Результаты и ограничения: [оптимизация 2.0.3](docs/OPTIMIZATION-2.0.3.md).
Фильтр остаётся добровольным; без него сохраняются прежние правила.
Добавление игры не гарантирует исправление лагов, NAT, чата или всех её серверов.
Стабильная опубликованная версия пока **2.0.2**.
[Изменения кандидата 2.0.3](docs/RELEASE-2.0.3.md).

### FOG Prime 2.0.2

Новая версия для автоматической настройки доступа к **Discord и YouTube**:

В v2.0.2 приложение запускает подключение автоматически и показывает один
понятный статус без выбора профилей и технических этапов. При кратком сбое
проверки продолжаются в фоне; при длительном сбое доступна кнопка «Повторить».
Закрытие окна останавливает процессы FOG. Обновления предлагаются через
официальную страницу GitHub, без скрытой установки.

Сохранены существующие списки адресов: `robrun.com`, `kefirgames.com`,
`robrun.wiki`, `robrun.site`, `robrun.org`, `epicgames.com`, `epicgames.dev`,
`easyanticheat.net`. Рабочий движок и правила Discord/YouTube сохранены из v2.0.1.
Экспериментальный FOG Native в этот релиз не входит. Добавление доменов не
гарантирует работу авторизации, голоса или игровых чатов.

### Игровой фильтр кандидата 2.0.3

В интерфейсе есть ручной переключатель **«Игровой фильтр»**, целевые игры -
**ROB RUN и Warframe** в общем встроенном каталоге. По умолчанию фильтр выключен; выбор сохраняется между запусками.
Переключение переподключает только FOG, не перезапускает игру.
При включении используются дополнительные TCP/UDP-правила из 1.10.4 для портов
1024-65535 по закреплённому IP-списку, с исключением локальных сетей и приоритетом
Discord/YouTube. Это фильтр адресов, не процессов: он может затрагивать другие
приложения на тех же CDN/хостинговых сетях. При здоровом внешнем VPN FOG сохраняет
прямой режим, не вмешиваясь в туннель; выбор фильтра применяется при работе Engine.
ROB RUN добавлен как целевая игра для проверки, не как гарантия устранения лагов.

- автоматический перебор с возвратом к лучшему найденному профилю;
- отдельный учёт ошибок голосового соединения Discord;
- быстрый повторный запуск с запоминанием проверенного профиля;
- первая проверка сразу после запуска движка, без ожидания фонового таймера;
- обновлённый интерфейс с анимацией соединения и восстановлением WebView2;
- предупреждение о VPN без его принудительного отключения.

**[Скачать v2.0.2](https://github.com/DenisRapira/FOG-Prime/releases/tag/v2.0.2)** ·
[Что изменилось](docs/RELEASE-2.0.2.md) ·
[Предыдущая v2.0.1](https://github.com/DenisRapira/FOG-Prime/releases/tag/v2.0.1)

Результат зависит от сети. Проверки доступности не гарантируют слышимость голоса,
воспроизведение видео или совместимость с каждым античитом. Telegram и Steam
не входят в целевые сервисы этой версии.

Download the current Windows x64 package from **[GitHub Releases](https://github.com/DenisRapira/FOG-Prime/releases/latest)**.

1. Download `FOG-Prime-<version>-win-x64.zip` or the current Windows x64 archive.
2. Extract the complete archive to a dedicated folder.
3. Run `FOG Prime.exe` and accept the Windows elevation prompt.
4. Keep the FOG Prime window open while the connection profile is needed.

Only use packages published under this repository's Releases page. Do not run individual backend files (`app/runtime/` in the candidate) or download builds from comments, direct messages, or third-party mirrors.

The 2.0.3 candidate root contains only `FOG Prime.exe`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `notices/` and `app/`. Backend executables, runtime, configuration and integrity/provenance JSON live in `app/`; dependency documents remain in `notices/`. The main EXE and window use the bundled FOG icon. Close the old FOG instance before extracting the new candidate into a separate empty folder; do not overlay it onto an older package. Internal reports remain in the repository, not the ZIP. This candidate layout does not change the published 2.0.2 archive.

## Key features

- Automatic selection from a small, bundled set of network profiles.
- Synthetic endpoint/CDN checks plus recent local Discord connection-state evidence when available, not an audio test.
- Certificate-validated TLS and Gateway HELLO/heartbeat checks.
- DNS diagnostics without forcing a permanent system DNS change.
- .NET Windows Agent with native Engine supervision and automatic recovery.
- Authenticated, current-user-only, per-session named-pipe IPC with a fixed command protocol in the candidate.
- SHA-256 integrity verification for every packaged runtime file.
- FOG Engine source and packet sample provenance pinned to recorded revisions.
- No FOG-operated backend or user account; external probes/update checks and local diagnostics are disclosed in the privacy policy.
- Portable lifecycle: closing the UI also shuts down the Agent and Engine.

## How it works

```text
FOG Prime.exe
      | authenticated local named pipe
      v
app/FOG.Agent.exe -- integrity, profiles, health checks, recovery
      | direct process start
      v
app/runtime/FOG.Engine.exe + WinDivert
```

The UI never receives raw engine arguments and never launches shell scripts. The Agent accepts only a fixed set of commands, validates trusted manifests and hashes, and starts one of the bundled profiles through a direct process API. See [ARCHITECTURE.md](ARCHITECTURE.md) for the trust boundaries and process lifecycle.

## System requirements

| Requirement | Supported configuration |
| --- | --- |
| Operating system | Windows 10 or Windows 11 x64 |
| Runtime | Microsoft Edge WebView2 Runtime |
| Permissions | Administrator access for the network driver |
| Windows service | Base Filtering Engine must be available |
| Package format | Portable ZIP from GitHub Releases |

## Security and privacy

FOG Prime runs locally and does not require a FOG-operated backend. The UI-to-Agent channel uses a random 256-bit session token and a named pipe restricted to the current Windows user. Runtime manifests and files are verified before launch. The repository contains a GitHub Actions release workflow; its presence alone does not establish the provenance or signature trust of a downloaded archive.

- Report vulnerabilities privately through [GitHub Security Advisories](https://github.com/DenisRapira/FOG-Prime/security/advisories/new).
- Remove tokens, private domains, IP addresses and personal data before posting logs.
- Read [SECURITY.md](SECURITY.md) and the [privacy policy](docs/PRIVACY.md).

## Build from source

The candidate uses .NET 10, C#, WPF/WebView2, C, PowerShell and WinDivert. Use the SDK selected by `global.json`; a matching global SDK is sufficient and no machine-specific SDK directory is required.

```powershell
dotnet restore .\FOG.Prime.sln --locked-mode
dotnet build .\FOG.Prime.sln -c Release --no-restore
.\build\verify-repository.ps1 -SkipBuild
```

Building the native Engine requires Cygwin x64 with `gcc-core`, `make`, and `zlib-devel`. Source build, packaging, integrity checks and integration-test limitations are in [docs/BUILDING.md](docs/BUILDING.md). These instructions do not claim byte-for-byte reproducibility.

## Troubleshooting

- **The app requests administrator access:** WinDivert needs elevation to inspect and redirect the selected network traffic.
- **The UI does not open:** install or repair Microsoft Edge WebView2 Runtime.
- **A connectivity check still fails:** inspect the diagnostics and Windows Base Filtering Engine (`BFE`), then retry. A detected healthy tunnel can use direct mode; a degraded tunnel may still cause the candidate to start Engine. See the troubleshooting guide before changing VPN settings.
- **The package reports an integrity error:** download a fresh archive from Releases and extract all files together.
- **The connection stops after closing FOG Prime:** this is expected; the portable UI owns the Agent and Engine lifecycle.

See [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) before opening an issue.

## Frequently asked questions

### Is FOG Prime a VPN or proxy service?

No. FOG Prime is a local Windows networking tool. It does not provide a remote VPN endpoint, hosted proxy, account, or subscription.

### Does FOG Prime collect analytics or browsing history?

No built-in analytics uploader was identified in the reviewed UI/Agent sources. Health checks contact Discord/YouTube endpoints, the UI automatically queries GitHub for updates, and the Agent reads recent local Discord connection-state evidence. Local diagnostics can contain network/IP information. See the [privacy policy](docs/PRIVACY.md).

### Does it support macOS, Linux, Android or iOS?

The current release targets Windows 10/11 x64 because it depends on WinDivert and a Windows Agent. Other platforms are not currently supported.

### Where can I verify the latest version?

Use the [latest GitHub Release](https://github.com/DenisRapira/FOG-Prime/releases/latest) and [release status](docs/release-status.json). The verified v2.0.2 release has no published checksum asset. A locally computed SHA-256 is useful for identifying a file but cannot authenticate it without an independently trusted expected value. Compare changes in [CHANGELOG.md](CHANGELOG.md).

## Project documentation

- [Architecture](ARCHITECTURE.md)
- [Build guide](docs/BUILDING.md)
- [Release status](docs/release-status.json)
- [Patch and release acceptance](docs/PATCH-CHECKLIST.md)
- [Project audit, fixes and remaining work (2026-11-27)](docs/AUDIT-2026-11-27.md)
- [Package size experiment](docs/SIZE-STUDY-2.0.3.md)
- [Product landscape and proposed roadmap](docs/MARKET-ROADMAP-2026-11-27.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Security policy](SECURITY.md)
- [Privacy policy](docs/PRIVACY.md)
- [Contributing](CONTRIBUTING.md)
- [Support](SUPPORT.md)
- [Changelog](CHANGELOG.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## Contributing and license

Issues and pull requests are welcome under the rules in [CONTRIBUTING.md](CONTRIBUTING.md). Original FOG Prime code is licensed under the [MIT License](LICENSE), Copyright (c) 2026 DenisRapira. The Engine, WinDivert, Cygwin and Microsoft components retain their separate terms in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). See the [distribution checklist](docs/LICENSE-COMPLIANCE.md) for outstanding publication gates; the repository is not binary-only.

Project website: **[denisrapira.github.io/FOG-Prime](https://denisrapira.github.io/FOG-Prime/)**

## Verification limits

As of 2026-11-27, the candidate Release build completed with zero warnings/errors and 97/97 scoped tests passed; these are not 97 live-network scenarios. Regression coverage includes serialized Start/Stop intent, cancelled requests and rejection of stale network-probe evidence. These source/build results do not verify a newly packaged ZIP; earlier candidate hashes identify historical artifacts only. A full native-code audit remains incomplete. Disposable-VM driver tests and live VPN transitions have not been run; end-to-end voice/audio, NAT behavior and anti-cheat compatibility remain unverified. Antivirus results are unavailable: Defender was stopped, no VirusTotal API key was available, and uploads were not authorized. No clean-AV, ban-safety, latency or universal compatibility guarantee is made.
