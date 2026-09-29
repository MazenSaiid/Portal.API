# Portal — working notes for AI assistants

Customer Support CRM. Backend here (`Portal.API`, .NET 10), frontend in `../Portal.FrontEnd` (Angular 20).
Assessment rubric `AI_FullStack_Assessment_Rubric_v1.0.xlsx` drives the process. Mandatory scope is in its "Task Scope" sheet.

## Process (required)
- Spec first: `docs/specs/00N-*.md` (assumptions, rules, API, acceptance criteria), then `docs/plan/00N-*.md` (tasks), then code.
- Focused commits per layer (conventional commits). Tests for every acceptance criterion. Update `docs/verification.md`.
- Build one module, get the user's sign-off, then continue.

## Backend conventions
- Layers: Domain → Application → Infrastructure → API. Follow `docs/architecture.md` "Adding a new module".
- Services validate with FluentValidation (`ValidateAndThrowAsync`) and throw `NotFoundException` / `ConflictException` /
  `BusinessRuleException`. Never return error objects from controllers; `GlobalExceptionHandler` maps them.
- Every endpoint gets `[HasPermission(...)]`. New keys go in `Permissions` + `PermissionRegistry`, never role-name checks.
- Call `PermissionCache.InvalidateAll()` after changing grants, roles, user status or user role.
- Use `DateTime.UtcNow`. SQLite is used in tests, so avoid SQL Server-only query features and `DateTimeOffset` comparisons in queries.
- Run `dotnet test` before committing.

## Frontend conventions
- Only CSS variables from `src/styles/_tokens.scss`. No raw colors, font sizes or spacing.
- Compose pages from the shared classes and `shared/ui` components. Toast on every mutation, `ConfirmService` for destructive actions.
- Standalone components, signals, `input()`/`output()`, OnPush, reactive forms. No arrow functions in templates.
- Gate UI with `*appHasPermission` and routes with `permissionGuard`. Map API field errors with `applyServerErrors`.
- Run `npm run test:ci` and `npm run build` before committing.
