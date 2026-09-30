# Plan 001 — Identity & Access: technical plan and task breakdown

Spec: [001-identity-access.md](../specs/001-identity-access.md)

## Technical approach

* **Identity** — `ApplicationUser : IdentityUser<Guid>`, `ApplicationRole : IdentityRole<Guid>` with an explicit `ApplicationUserRole` join so EF navigations can be queried.
* **Permissions** — `PermissionRegistry` (Domain) is the single source of truth. `PermissionSynchronizer` inserts new keys, updates descriptions and removes obsolete ones on startup, then grants everything to the Administrator role.
* **Enforcement** — `[HasPermission(...)]` builds a dynamic policy (`PermissionPolicyProvider`) → `PermissionAuthorizationHandler` resolves the user's effective permissions through `IPermissionService`, cached in `IMemoryCache` and invalidated as a whole whenever a grant changes.
* **Errors** — Application throws typed exceptions (`NotFoundException`, `ConflictException`, `BusinessRuleException`, FluentValidation `ValidationException`); a single `IExceptionHandler` maps them to ProblemDetails.
* **Validation** — FluentValidation validators in Application, invoked explicitly by services (easy to unit test, no magic).
* **Frontend** — Angular standalone components + signals. A shared SCSS token file drives every color, font size, radius and spacing value; reusable UI components (button, toggle, modal, confirm dialog, toast, pagination, form field) are the only building blocks pages use.

## Tasks

| # | Task | Layer | Done |
|---|---|---|---|
| T1 | Write spec, assumptions, acceptance criteria | Docs | ✅ |
| T2 | Scaffold solution: Domain / Application / Infrastructure / API / Tests | Backend | ✅ |
| T3 | Domain entities + `PermissionRegistry` | Domain | ✅ |
| T4 | `AppDbContext`, EF configurations, initial migration | Infrastructure | ✅ |
| T5 | Seeding: permission sync, Administrator & Agent roles, admin user | Infrastructure | ✅ |
| T6 | JWT issuing, login / me / change-password | Application + API | ✅ |
| T7 | Dynamic permission policy + cached permission service | API + Infrastructure | ✅ |
| T8 | Users service + controller (paging, filters, rules R1, R3, R4) | Application + API | ✅ |
| T9 | Roles service + controller + permission toggle (R2, R5, R6, R8) | Application + API | ✅ |
| T10 | Global exception handling → ProblemDetails | API | ✅ |
| T11 | Unit tests (validators, registry) + integration tests (AC1–AC7, AC11–AC13) | Tests | ✅ |
| T12 | Angular workspace, design tokens, shared UI kit, layout shell | Frontend | ✅ |
| T13 | Auth: login page, token interceptor, error interceptor, guards, `*hasPermission` | Frontend | ✅ |
| T14 | Users pages (list, filters, create/edit dialog, status, reset password, delete) | Frontend | ✅ |
| T15 | Roles pages (list, create/edit, permissions matrix with toggles) | Frontend | ✅ |
| T16 | Frontend unit tests (guard, directive, auth service) | Frontend | ✅ |
| T17 | README + architecture notes, verification checklist | Docs | ✅ |
