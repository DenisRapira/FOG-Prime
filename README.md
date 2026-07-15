# FOG Prime

[![CI](https://github.com/DenisRapira/FOG-Prime/actions/workflows/ci.yml/badge.svg)](https://github.com/DenisRapira/FOG-Prime/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/DenisRapira/FOG-Prime?display_name=tag)](https://github.com/DenisRapira/FOG-Prime/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-black.svg)](LICENSE)

FOG Prime — локальное Windows-приложение для автоматической настройки и проверки соединения Discord и YouTube. Пользователь видит только три общих этапа: проверка системы, автоматическая настройка и проверка доступа.

## Возможности

- автоматический выбор из нескольких ограниченных сетевых профилей;
- рабочая голосовая связь Discord и доступ к YouTube через TLS/QUIC;
- проверки DNS, Discord Gateway/API/CDN и YouTube;
- собственный Windows Agent с автоматическим запуском и восстановлением Engine после сбоя;
- аутентифицированный локальный IPC без передачи произвольных команд и путей;
- встроенная цепочка доверия UI → Agent → manifest → runtime;
- проверка SHA-256 каждого runtime-файла перед запуском;
- сборка FOG Engine из закреплённой ревизии исходников;
- отсутствие обязательного удалённого сервера и встроенной telemetry.

## Установка

1. Откройте [Releases](https://github.com/DenisRapira/FOG-Prime/releases/latest).
2. Скачайте `FOG-Prime-<version>-win-x64.zip`.
3. Распакуйте архив в отдельную папку.
4. Запустите `FOG Prime.exe` и подтвердите системный запрос Windows.

Не запускайте отдельные файлы из папки `runtime` вручную. Не скачивайте сборки из комментариев, личных сообщений и сторонних зеркал.

## Требования

- Windows 10 или Windows 11 x64;
- Microsoft Edge WebView2 Runtime;
- права администратора для работы сетевого драйвера;
- доступ к системной службе Base Filtering Engine.

## Архитектура

```text
FOG Prime.exe
      │ authenticated local named pipe
      ▼
FOG.Agent.exe ── integrity / profiles / health / recovery
      │ direct process start
      ▼
FOG.Engine.exe + WinDivert
```

UI не получает аргументы движка и не запускает shell-скрипты. Agent принимает только фиксированный набор команд, проверяет встроенные доверенные хэши и сам выбирает разрешённый профиль. Закрытие UI завершает Agent и Engine. Подробнее: [ARCHITECTURE.md](ARCHITECTURE.md).

## Сборка

Быстрая проверка .NET-части:

```powershell
dotnet restore .\FOG.Prime.sln --locked-mode
dotnet build .\FOG.Prime.sln -c Release --no-restore
.\build\verify-repository.ps1 -SkipBuild
```

Полная Windows-сборка Engine и release package описана в [docs/BUILDING.md](docs/BUILDING.md). GitHub Actions workflow `release.yml` выполняет те же шаги и публикует ZIP для тегов `v*`.

## Безопасность и приватность

- Уязвимости отправляйте приватно через [GitHub Security Advisories](https://github.com/DenisRapira/FOG-Prime/security/advisories/new).
- Перед публикацией логов удаляйте токены, приватные домены, IP-адреса и персональные данные.
- Политики: [SECURITY.md](SECURITY.md) и [docs/PRIVACY.md](docs/PRIVACY.md).

## Лицензии

Код FOG Prime распространяется по MIT. FOG Engine основан на закреплённом upstream source, а WinDivert является отдельной dependency под LGPLv3/GPLv2. Обязательные уведомления находятся в [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) и [FOG.Engine/UPSTREAM.md](FOG.Engine/UPSTREAM.md).

## Поддержка

Перед созданием issue прочитайте [SUPPORT.md](SUPPORT.md). Pull requests принимаются по правилам из [CONTRIBUTING.md](CONTRIBUTING.md).
