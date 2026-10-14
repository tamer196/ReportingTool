# Reporting Tool

Web-based drag-and-drop reporting tool (Paca project **RT**). An Angular SPA calls .NET services
backed by SQL Server; the TDD stack (decision D-12) adds RabbitMQ, Hangfire, a cache (Valkey)
and search (OpenSearch) as stories need them (*ADR-002*). The most important quality attribute
is **auditable, access-controlled data**: every rule about who may see which data is enforced in
the services, never only in the UI.

Design sources live in Paca, not in this repo: *Architecture/TDD*, *Architecture/Developer
Setup & Pipelines*, *Business Analysis/10 - PRD & TDD Gap Analysis*.

## Build & run

```
scripts/init-local-secrets.ps1        # once per machine (.sh on macOS/Linux)
docker compose up --build             # SQL Server + API + web → http://localhost:8080

dotnet build ReportingTool.slnx       # must be 0 warnings / 0 errors (CI treats warnings as errors)
dotnet test --solution ReportingTool.slnx   # needs Docker: API tests run SQL Server in a container
dotnet format ReportingTool.slnx --verify-no-changes

cd src/Web && npm ci && npm start     # Angular on http://localhost:4300, proxies /api to :5080
npm run lint && npm run test:ci && npm run build
```

Hosting: Phase 1 runs entirely in Docker Compose on one machine (ADR-002, D-15); nothing is
deployed. The code lives at https://github.com/tamer196/ReportingTool (public): every change
goes through a pull request to the protected `main`, and GitHub Actions runs CI on each one.
The repository is public, so never commit secrets, internal hostnames or real client data.
Every component must be free to use today (D-16); add new ones to the licence inventory in
*ADR-002* §8. Milestone tags and releases follow *Delivery/00 - Milestones & Git Release Plan* §5.

## Architecture — follow these rules for ALL code in this repo

The .NET code follows a layered service architecture. The non-negotiables:

1. **Folder = namespace, 1:1**, every internal namespace listed in the project's
   `GlobalUsings.cs`. `Interfaces\{Concern}` is mirrored by `Services\{Concern}`.
2. **No code defaults for config.** Options classes are `sealed` with a `Section` const,
   DataAnnotations, and `ValidateOnStart()`. Missing config fails boot, never a silent default.
3. **Two error boundaries, no other catch.** HTTP: `ProblemDetailsExceptionHandler` (behind
   `UseExceptionHandler`). Everything else: `IGlobalExceptionHandler.Run/RunAsync`. The only
   other try/catch is the Program.cs bootstrap.
4. **`ILoggerService` only**: never `ILogger<T>`, never Serilog types outside Program.cs and
   `Platform/Extensions/PlatformHostingExtensions.cs`. Structured templates, never interpolated
   strings.
5. **Fail-fast at startup, fail-soft at runtime.**
6. **Secrets come from the vault mount** (`Secrets:Directory`, one file per key, e.g.
   `Database__ConnectionString`) or user-secrets locally. Never in appsettings, environment
   variables or pipeline variables.
7. **UTC everywhere (BR-14).** Time comes from the injected `TimeProvider`; `DateTime.Now`,
   `UtcNow` and friends are banned at compile time (`BannedSymbols.txt`). EF stores every
   `DateTime` through `UtcDateTimeConverter` (`StoreDateTimesAsUtc()`).
8. Style: file-scoped namespaces, `sealed` concrete classes, records for value shapes, XML docs
   that explain WHY, no TODO/FIXME in delivered code, naming `*Service`/`*Producer`/`*Consumer`.

Deliberate adaptations of the house template for a multi-service monorepo:
- Shared plumbing (logging, boundaries, secrets, health, UTC, migrations) lives once in
  `src/Platform/ReportingTool.Platform`, not copied into every service.
- Services are ASP.NET Core web hosts (`WebApplication`), because HTTP is their main surface,
  rather than Worker hosts with embedded Kestrel listeners.
- Containers log to stdout only (no file sink); Dev/Test log one JSON object per line.

Front end: standalone components, signals, zoneless, `inject()`, built-in control flow;
services own HTTP; design tokens only in component CSS (`src/Web/STYLE.md`).

## Project map

| Path | Responsibility |
| --- | --- |
| `src/Platform/ReportingTool.Platform` | Shared plumbing every service references |
| `src/Services/ReportingTool.Api` | First deployable service; hello-world slice (`/api/greetings`), `/api/system/info` |
| `src/Services/ReportingTool.Api/Persistence/Migrations` | EF Core migrations (generated) |
| `src/Web` | Angular 21 SPA + nginx image config |
| `tests/*` | xUnit v3 on Microsoft.Testing.Platform; API tests use Testcontainers SQL Server |
| `compose.yaml`, `scripts/` | Local environment and local secrets |
| `.github/workflows` | `ci.yml` (pull requests and main), `cd.yml` (manual, builds images only, deploys nothing) |
| `CHANGELOG.md`, `docs/releases/` | One entry and one release-notes file per milestone tag (*Delivery/00* §5) |

## Environment pins & gotchas

- .NET 10 SDK (`global.json`), Node 24 (`src/Web/.nvmrc`), SQL Server 2022 CU27 (one image tag,
  pinned in `compose.yaml` and `ApiFactory.cs`). The app version is `<Version>` in
  `Directory.Build.props`: the milestone `main` is working towards. `/api/system/info` appends the
  commit; `.git/` is not in the image build context, so Compose needs `RT_SOURCE_REVISION`
  (`git rev-parse HEAD`) to show it, and shows `+local` without it.
- Ports: web 8080 (compose), API 5080 (compose and `dotnet run`), Angular dev server 4300, SQL 1433.
  On the shared agent machine each workspace overrides them with `RT_WEB_PORT`, `RT_API_PORT` and
  `RT_SQL_PORT`, and names its stack with `COMPOSE_PROJECT_NAME`, so parallel stacks never collide.
- `dotnet user-secrets` is per Windows user, so agents on the shared machine share it: prefer
  `docker compose up` (secrets live in this clone's `.secrets/`) over `dotnet run` against SQL.
- `ASPNETCORE_ENVIRONMENT`: `Development` = a developer machine (including compose);
  `Dev` and `Test` = shared environments (QA's test stack is RT-121); `Testing` = integration tests.
- Adding a migration: `dotnet ef migrations add <Name> --project src/Services/ReportingTool.Api
  --output-dir Persistence/Migrations` (needs no database; see `DesignTimeReportingDbContextFactory`).
- The `Greeting` table is the hello-world slice only; remove it once Sprint 1 owns real tables.
