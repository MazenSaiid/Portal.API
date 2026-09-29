# Architecture notes

## Overview

```
 Angular SPA ──HTTP/JSON + JWT──▶ Portal.API ──▶ Portal.Application ──▶ Portal.Domain
 (Portal.FrontEnd)                (controllers,    (services, DTOs,        (entities,
                                   authz, errors)   validators, rules)      permission registry)
                                        │                  ▲
                                        ▼                  │ interfaces
                                 Portal.Infrastructure ────┘
                                 (EF Core + SQL Server, Identity, JWT, seeding)
```

Layered ("clean") architecture with one rule: dependencies point inwards. Application defines the interfaces it
needs (`IApplicationDbContext`, `IJwtTokenGenerator`, `ICurrentUser`); Infrastructure and API implement them.
Four projects is the smallest split that keeps HTTP concerns, business rules and persistence separate without
adding a mediator, repositories or CQRS, which this size of system does not need.

## Request flow

1. **Authentication** — `JwtBearer` validates signature, issuer, audience and lifetime. The token carries only
   identity (`sub`, `email`, `name`, and `role` for display). Access tokens last 15 minutes and are renewed with a
   rotating refresh token (see *Sessions* below).
2. **Authorization** — endpoints declare `[HasPermission(Permissions.Users.Edit)]`.
   `PermissionPolicyProvider` builds the policy on the fly; `PermissionAuthorizationHandler` asks
   `IPermissionService` for the user's effective permissions.
3. **Controller** — thin: binds the request and calls one service method.
4. **Service** — validates the input (FluentValidation), enforces business rules, uses EF Core / Identity managers.
5. **Errors** — services throw typed exceptions; `GlobalExceptionHandler` turns them into RFC 7807 ProblemDetails:

   | Exception | HTTP |
   |---|---|
   | `ValidationException` | 400 + `errors` per field (camelCase, same names as the JSON) |
   | `AuthenticationFailedException` | 401 |
   | `NotFoundException` | 404 |
   | `ConflictException` | 409 |
   | `BusinessRuleException` | 422 |
   | anything else | 500, logged, no internals leaked |

## Permission model — key decisions

| Decision | Why |
|---|---|
| **Permissions are defined in code** (`PermissionRegistry`) and synced to the `Permissions` table on startup | A permission only has meaning if some code checks it. A new module adds its keys to the registry and they appear in the UI with toggles, with no migration or frontend change. A unit test fails if a constant is not registered. |
| **Roles and grants are data** (`Roles`, `RolePermissions`) | Fully dynamic: admins create roles and toggle permissions at runtime. |
| **Checks are per request, cached, not in the JWT** | Revoking a permission or deactivating a user takes effect on the next request instead of when the token expires. `PermissionCache` holds each user's set for 10 minutes and is cleared as a whole on any change to grants, roles, user status or user role. Clearing everything is simple and correct, and changes are rare. |
| **Checks are "any of"** | `[HasPermission(A, B)]` allows either one (e.g. the role dropdown is needed by both user managers and role managers). Stack several attributes for "all of". |
| **One role per user** | Matches the requirement and keeps effective permissions easy to reason about. The Identity join table would allow more later. |
| **Administrator is a system role** | Always holds every permission (re-granted at startup, so new permissions are included), cannot be renamed or deleted, and its grants cannot be edited. Together with rules R3/R4 the system can never lock itself out. |

The cache is in-memory (`IMemoryCache`), which is right for a single API instance. With several instances,
swap `PermissionCache` for a distributed cache or a short TTL; nothing else changes.

## Sessions (refresh tokens) — [spec 002](specs/002-refresh-tokens.md)

* Login returns a 15-minute JWT and a 7-day refresh token. Only the refresh token's SHA-256 hash is stored (`RefreshTokens` table).
* `POST /api/auth/refresh` **rotates**: the used token is revoked and linked (`ReplacedByTokenHash`) to its successor.
  Presenting a rotated token again is treated as theft, and every session of that user is revoked.
* Logout, deactivation and an admin password reset revoke sessions. Changing your own password revokes all sessions and returns a new one.
* The SPA renews the token one minute before expiry, and again on any 401, then retries the request once.
  Concurrent requests share a single refresh call, and tabs share tokens through `localStorage` events, so one tab
  never replays a token another tab has rotated.

## Auditing and files (added with [spec 003](specs/003-customer-management.md))

* Entities deriving from `AuditableEntity` get `CreatedAt/By` and `UpdatedAt/By` stamped in `AppDbContext.SaveChanges`
  from `ICurrentUser`, so no service can forget them. Audit columns are plain ids without FKs, so deleting a user never
  rewrites history (names show as "Former user"). The audit-log module builds on this hook.
* Uploaded files go through `IFileStorage`. `LocalFileStorage` writes to `Storage:RootPath` under random names.
  Application code only sees a `Stream`, so moving to blob storage is one new class. Downloads are always
  `Content-Disposition: attachment` with a content type taken from an extension allow-list, and every response
  carries `X-Content-Type-Options: nosniff`.
* Enums travel as strings (`"type": "Company"`) for readable payloads and simple TypeScript unions.

## Tickets (added with [spec 004](specs/004-ticket-management.md))

* **One workflow definition.** `TicketWorkflow` (Domain) holds the transition table. The service enforces it,
  `TicketDto.AllowedStatuses` exposes it, and the UI renders only those buttons, so the three can't disagree.
* **History is written by the server in the same save as the change** (`TicketHistoryEntry`, sequential id for stable
  ordering). Names are stored as text snapshots so history stays readable after renames or deletions.
* **Agents are derived from permissions.** An assignee is any active user whose role grants `Tickets.Work`; there is
  no separate "is agent" flag to keep in sync.
* Priority and status are stored as numbers so sorting follows severity and workflow order.
* Delete behaviours: a customer or category in use is *restricted*, a deleted assignee *sets null*, and history
  *cascades* with its ticket.

## Security measures

* Identity password hashing, password policy, and account lockout (5 attempts → 5 minutes).
* Generic "Invalid email or password" message. Account state is only revealed after a correct password.
* Fixed-window rate limit on `POST /api/auth/login` per IP.
* The JWT signing key is not in source-controlled production config. Options are validated at startup
  (`ValidateOnStart`), so a missing or short key stops the app.
* CORS restricted to the configured frontend origin. HTTPS redirect and HSTS outside development.
* The frontend attaches the token only to API URLs and follows only same-origin return URLs after login.
* Server-side authorization on every endpoint. The UI hiding buttons is a convenience, not the protection.

## Data

SQL Server via EF Core code-first migrations (`src/Portal.Infrastructure/Persistence/Migrations`). Identity tables are
renamed (`Users`, `Roles`, `UserRoles`…). All timestamps are UTC and returned with `Z`.

```bash
# add a migration after changing the model
dotnet ef migrations add <Name> -p src/Portal.Infrastructure -s src/Portal.API -o Persistence/Migrations
```

## Frontend architecture

See [`../Portal.FrontEnd/README.md`](../../Portal.FrontEnd/README.md). In short: standalone components and signals.
A single token file drives every visual value. A small shared UI kit (modal, toast, confirm, toggle, pagination,
form field) means every page looks and behaves the same. The HTTP layer normalises all errors into
`ApiError`, so pages only handle field errors on forms.

## Adding a new module (checklist)

1. Write `docs/specs/00N-<module>.md` (scope, assumptions, rules, API, acceptance criteria) and `docs/plan/00N-...`.
2. Add permission keys to `Permissions` + `PermissionRegistry` (Domain).
3. Entities (Domain) → EF configuration + migration (Infrastructure).
4. DTOs, validators, service with business rules (Application). Register it in `DependencyInjection`.
5. Thin controller with `[HasPermission]` (API).
6. Integration tests for each acceptance criterion; unit tests for validators and rules.
7. Frontend: `features/<module>/` with an `*.api.ts` service and pages built from the shared UI kit; route with
   `permissionGuard`; menu entry in `layout/navigation.ts`.
