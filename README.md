# Flenta Backend

ASP.NET Core / .NET 10, Supabase Auth, PostgreSQL та Entity Framework Core 10.
Desktop звертається лише до API; рядок підключення БД і ключ шифрування карток
не передаються у фронтенд.

## Де що знаходиться

| Репозиторій | Відповідальність |
| --- | --- |
| Flenta_Backend | Доменні сутності, AppDbContext, конфігурації EF, міграції та API |
| Flenta_Infra | Запуск PostgreSQL, Docker Compose і шаблони параметрів підключення |
| Flenta_Desktop | HTML-форми для тестування API, без банківської схеми БД |

`src/Flenta.Infrastructure/Persistence/AppDbContext.cs` містить DbSet для рахунків,
карток, операцій і проводок. Fluent API винесений в окремі
`IEntityTypeConfiguration<T>` у `Persistence/Configurations/`.
Міграції та snapshot знаходяться в `Persistence/Migrations/`.

Схема `banking` та ID початкової міграції `20261025110524_InitialBanking` збережені.
Перейменування BankingDbContext у AppDbContext не перестворює таблиці та не
потребує повторної початкової міграції. Журнал `__EFMigrationsHistory` залишається
на попередньому місці, у `public`, щоб наявні інсталяції розпізнавали свою історію.

Старі SQL-файли `Flenta_Infra/db/migrations/001_init.sql` і `002_extended.sql`
описують іншу схему в `public`. EF не керує цими таблицями, не перейменовує і
не видаляє їх. KYC, savings та інші старі таблиці не оголошені реалізованими
функціями поточного API. Нові можливості додавайте через сутності й EF-міграції.
Ідентичність користувачів зараз забезпечує Supabase Auth, а не `public.users`.

## Підключення

Встановіть .NET SDK 10.0.400 або новіший patch у тому самому feature band.
Версія локального `dotnet-ef` закріплена в `.config/dotnet-tools.json` і збігається
з EF Core 10.0.13; глобальний інструмент не потрібен.

Для першого налаштування скопіюйте `.env.example` у `.env.local`, якщо такого
файлу ще немає. Заповніть `ConnectionStrings__AppDb`, параметри Supabase та
постійний `Banking__CardEncryptionKey`. Обидва локальні env-файли ігноруються Git.

Пріоритет рядка підключення: `ConnectionStrings:AppDb`, потім `DB_CONNECTION_STRING`,
потім сумісний зі старою конфігурацією `Banking:ConnectionString`.

Factory EF читає `appsettings.json`, файл середовища, `.env`, `.env.local` та
змінні процесу, які перевизначають файли. Міграції можна створювати без запуску
API і без доступу до Supabase; потрібен налаштований рядок підключення, але для
генерації міграції сам сервер PostgreSQL не потрібен.

**Запущений API читає стандартну конфігурацію ASP.NET Core.** Файли `.env.local`
автоматично читає лише EF factory, тому при запуску API експортуйте потрібні
змінні через термінал, IDE або ваш launcher. Не зберігайте пароль у командах Git.

## Міграції з термінала Zed

Запускайте з кореня Flenta_Backend у PowerShell:

```powershell
./scripts/ef.ps1 list                     # список міграцій
./scripts/ef.ps1 add AddSavingsGoals      # після зміни моделі
./scripts/ef.ps1 check                    # чи відповідає snapshot моделі
./scripts/ef.ps1 update                   # застосувати відсутні міграції
./scripts/ef.ps1 script                   # ідемпотентний SQL у artifacts/
./scripts/ef.ps1 bundle                   # окремий executable для розгортання
```

Скрипт відновлює закріплену версію EF, обирає AppDbContext і правильні проєкти.
Згенеровані SQL і bundle ігноруються Git. Контекст і міграції не дублюються в Infra.
Bundle можна запускати поза checkout; передайте `ConnectionStrings__AppDb`
через змінну середовища на машині розгортання.
Для macOS/Linux можна користуватися PowerShell 7 або стандартними командами:

```sh
dotnet tool restore
dotnet ef migrations list --project src/Flenta.Infrastructure --startup-project src/Flenta.Infrastructure --context AppDbContext
dotnet ef database update --project src/Flenta.Infrastructure --startup-project src/Flenta.Infrastructure --context AppDbContext
```

Для нової сутності додайте клас у Domain, її конфігурацію в Infrastructure і,
за потреби, DbSet у AppDbContext. Створіть міграцію командою `add`, перегляньте
Up/Down та запустіть тести. Уже застосовані міграції не редагуйте.
Перенесення поточної початкової міграції змінило лише namespace/context metadata.

API не запускає міграції автоматично: у розгортанні застосовуйте перевірений
SQL/bundle окремим кроком з обліковим записом для DDL. Runtime-обліковому запису
надавайте лише потрібні права; поточна приватна схема використовує RLS без
клієнтських політик, тому окреме налаштування runtime-ролі повинно це враховувати.

## Перевірки

```powershell
dotnet restore --locked-mode
dotnet test --nologo
./scripts/ef.ps1 check
```

Для PostgreSQL integration tests задайте `BANKING_TEST_CONNECTION` на окремий
тестовий сервер/роль з правом CREATE DATABASE. Тести створюють власні випадково
названі бази, застосовують міграції та видаляють тільки ці тестові бази.
Без цієї змінної PostgreSQL-тести будуть явно пропущені.
