# Flenta Backend

Каркас ASP.NET Core API на .NET 10. Это этап архитектуры: регистрация,
авторизация, профиль, банковские операции и интеграция Supabase ещё не реализованы.

## Структура

```text
src/
  Flenta.Domain/          Сущности, объекты-значения и бизнес-ошибки.
  Flenta.Application/     Сценарии приложения и интерфейсы внешних зависимостей.
  Flenta.Infrastructure/  Реализации интерфейсов и внешние интеграции.
  Flenta.Api/             Контроллеры, конфигурация HTTP и обработка ошибок.
tests/
  Flenta.Tests/           Тесты домена, границ архитектуры и HTTP API.
```

Направление ссылок между проектами:

```text
Application    -> Domain
Infrastructure -> Application, Domain
Api            -> Application, Infrastructure
```

Domain не зависит от других проектов и NuGet-пакетов. Application не знает о
Supabase, HTTP или конкретной базе данных. Api ссылается на Infrastructure для
регистрации реализаций в DI-контейнере; контроллеры используют интерфейсы Application.

## ООП и паттерны

- **Инкапсуляция:** идентификатор `Entity` нельзя изменить после создания;
  `EmailAddress` создаётся только через проверяющий фабричный метод и неизменяем.
- **Value Object:** адреса сравниваются по значению, а не по ссылке на объект.
  Проверка синтаксиса не подтверждает существование почты и не заменяет её верификацию.
- **Dependency Inversion / Adapter:** Application объявляет `IClock`,
  Infrastructure реализует его через `SystemClock`.
- **Dependency Injection:** `Program.cs` собирает приложение; `StatusController`
  получает `IClock` через конструктор. В тестах часы заменяются фиксированными.
- **Repository:** конкретные интерфейсы репозиториев появятся вместе с
  соответствующими сущностями и сценариями. Универсального CRUD-репозитория сейчас нет.

`Application/Features` предназначена для сценариев по функциям.
`AddApplication` — точка регистрации будущих сценариев; пока она не добавляет сервисов.
Папки `Infrastructure/Authentication`, `Persistence` и `Configuration` зарезервированы
для следующего этапа и пока содержат только `.gitkeep`.

## Запуск

Нужен SDK **.NET 10.0.402** (версия закреплена в `global.json`). Все команды ниже
выполняются из корня `Flenta_Backend`.

```sh
dotnet restore Flenta.Backend.sln --locked-mode
dotnet build Flenta.Backend.sln --no-restore --configuration Release
dotnet test Flenta.Backend.sln --no-build --no-restore --configuration Release
```

Для текущего облачного окружения сначала активируйте установленный SDK и каталоги:

```sh
export DOTNET_ROOT=/workspace/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.dotnet-cli
export NUGET_PACKAGES=/workspace/.nuget/packages
export TMPDIR=/workspace/.dotnet-tmp
export XDG_RUNTIME_DIR=/workspace/.dotnet-tmp
export XDG_DATA_HOME=/workspace/.dotnet-cli/share
export XDG_CONFIG_HOME=/workspace/.dotnet-cli/config
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$TMPDIR"
chmod 700 "$XDG_RUNTIME_DIR"
```

В облачной среде домашний каталог доступен только для чтения. Переменные XDG
переносят служебные данные и файлы синхронизации .NET в доступные каталоги.

API для разработки:

```sh
dotnet run --project src/Flenta.Api --no-launch-profile --configuration Release --no-build -- \
  --environment Development --urls http://127.0.0.1:5080
```

Проверки из другого терминала:

```sh
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/api/status
curl --fail http://127.0.0.1:5080/openapi/v1.json
```

- `GET /health` — работоспособность процесса; внешние сервисы пока не проверяет.
- `GET /api/status` — JSON с именем сервиса, статусом и временем UTC.
- `GET /openapi/v1.json` — документ OpenAPI, доступен только в Development.
  Графический Swagger UI на этом этапе не подключён.
- Ошибки возвращаются в формате Problem Details с `traceId`; доменные ошибки
  валидации дают HTTP 400 и стабильный `code`. Неожиданные ошибки дают HTTP 500
  без внутренних сообщений в ответе. Стандартные HTTP-ошибки также используют Problem Details.

Зависимости с NuGet фиксируются в `packages.lock.json`. Обновлять их следует явно,
проверять изменения lock-файлов и повторно запускать сборку и тесты.

## Конфигурация и дальнейшая работа

Сейчас для запуска не нужны база данных, Supabase или секреты. Настройки читаются
стандартным механизмом ASP.NET Core из `appsettings.json` и переменных окружения.
`AllowedHosts` ограничен `localhost` и `127.0.0.1`; при развёртывании его нужно явно
настроить под домен сервиса. Секреты не добавляются в исходники или appsettings.

Архитектура готовится в `dev` без коммита до проверки владельцем. После отдельного
одобрения коммита от `dev` создаётся `MarisAuth` для авторизации и профиля с Supabase.
Бизнес-сценарии из дальнейшего плана на этом этапе не реализуются.
