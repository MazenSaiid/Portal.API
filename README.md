# Portal — Customer Support CRM

Full-stack Customer Support CRM (feature map: [`azm_squad_customer_support_crm.pdf`](azm_squad_customer_support_crm.pdf)).

| Part | Folder | Stack |
|---|---|---|
| API | `Portal.API` (this folder) | .NET 10, ASP.NET Core Web API, EF Core 10, ASP.NET Core Identity, JWT, SQL Server |
| Web client | [`../Portal.FrontEnd`](../Portal.FrontEnd) | Angular 20 (standalone, signals, zoneless), SCSS design tokens |

## Module status

| # | Module (PDF) | Status | Spec |
|---|---|---|---|
| 10 | Security & Administration — users, roles, permissions | ✅ Done, awaiting design sign-off | [spec 001](docs/specs/001-identity-access.md) · [plan](docs/plan/001-identity-access-tasks.md) |
| 10 | Security & Administration — session renewal (refresh tokens) | ✅ Done | [spec 002](docs/specs/002-refresh-tokens.md) |
| 10 | Security & Administration — audit logs, system configuration | ⏳ Next | — |
| 1 | Customer management | ⏳ Planned (mandatory scope) | — |
| 2 | Ticket management | ⏳ Planned (mandatory scope) | — |
| 4 | Agent dashboard | ⏳ Planned (mandatory scope) | — |
| 3, 5–9, 11, 12 | Channels, SLA, knowledge base, AI, portal, reports, integrations, platform | Optional / bonus | — |

## Quick start

**Prerequisites:** .NET SDK 10, SQL Server (the default instance `.` or change the connection string), Node.js 22+.

```bash
# 1. API — creates and migrates the PortalDb database and seeds data on first run
cd Portal.API
dotnet run --project src/Portal.API --launch-profile http
#    → http://localhost:5120/swagger

# 2. Web client (second terminal)
cd ../Portal.FrontEnd
npm install
npm start
#    → http://localhost:4200
```

Sign in with **admin@portal.local / Admin@12345** (development seed only).

### Configuration

| Key | Where | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `appsettings.json` | Defaults to `Server=.;Database=PortalDb;Trusted_Connection=True` |
| `Jwt:Key` | `appsettings.Development.json` | **Empty in `appsettings.json` on purpose.** Outside development supply it via environment variable `Jwt__Key` or user-secrets (≥ 32 chars). The API refuses to start without it. |
| `Jwt:ExpiryMinutes` | `appsettings.json` | Access token lifetime, default 15 |
| `Jwt:RefreshTokenDays` | `appsettings.json` | Refresh token lifetime, default 7 |
| `Seed:AdminEmail` / `Seed:AdminPassword` | `appsettings*.json` | Admin is only created when a password is configured |
| `Cors:AllowedOrigins` | `appsettings.json` | `http://localhost:4200` |
| `Database:InitializeOnStartup` | `appsettings.json` | Migrate + seed at startup (idempotent). Turn off if migrations are applied by a pipeline. |
| `RateLimiting:LoginPermitsPerMinute` | optional | Default 10 login attempts / minute / IP |
| `RateLimiting:RefreshPermitsPerMinute` | optional | Default 60 refreshes / minute / IP |

## Tests

```bash
dotnet test                                 # backend: 55 unit + integration tests
cd ../Portal.FrontEnd && npm run test:ci    # frontend: 27 unit tests (headless Chrome)
```

Integration tests start the real API in-process (`WebApplicationFactory`) on an in-memory SQLite database,
so they need no SQL Server. See [docs/verification.md](docs/verification.md) for what each test proves and the manual
end-to-end checklist.

## Repository layout

```
docs/
  specs/          Specification per module: scope, assumptions, rules, API contract, acceptance criteria
  plan/           Technical approach and task breakdown per module
  architecture.md Design overview and key decisions
  verification.md How each acceptance criterion is verified
src/
  Portal.Domain/          Entities and the permission registry — no dependencies on other layers
  Portal.Application/     Use cases (services), DTOs, validators, business rules, interfaces
  Portal.Infrastructure/  EF Core DbContext, migrations, Identity stores, JWT, seeding
  Portal.API/             Controllers, permission authorization, error handling, composition root
tests/
  Portal.Tests/           Unit tests + HTTP-level integration tests
```

Read [docs/architecture.md](docs/architecture.md) for the design and the reasoning behind it.

## Working process

Every module follows the same loop: **spec → plan/tasks → implementation → tests → verification → review**.
The spec and plan are committed before the code, and each layer lands in its own focused commit.
