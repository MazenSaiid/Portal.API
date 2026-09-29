# Spec 001 — Identity & Access (Users, Roles, Permissions)

| Item | Value |
|---|---|
| Source module | PDF §10 *Security & Administration* — Users and roles, Permissions |
| Rubric scope | Mandatory — *Authentication and authorization* (authentication plus at least one role/permission boundary) |
| Status | Implemented — awaiting design confirmation |

## 1. Goal

Give administrators a fully dynamic way to control **who can do what** in the Portal:

* **Users** sign in with email + password (ASP.NET Core Identity, JWT bearer tokens).
* Every user is assigned **exactly one role**.
* Every role owns a **set of permissions**. Permissions are toggled on/off per role from the UI.
* Every API endpoint and every UI page/button is protected by a **permission**, never by a hard-coded role name.

## 2. Assumptions

| # | Assumption | Why |
|---|---|---|
| A1 | A user has exactly **one** role (Identity supports many; we enforce one). | Matches the requirement "a role is assigned to a user" and keeps the permission model predictable. |
| A2 | Permissions are **defined in code** (`PermissionRegistry`) and **synced to the database** at startup. Roles and role→permission grants are fully data-driven. | A permission only has meaning if code checks it; letting admins invent permission keys that nothing enforces would be misleading. New modules just add entries to the registry — no migration, no UI change. |
| A3 | The seeded **Administrator** role is a *system role*: it always holds every permission, cannot be renamed, deleted, or have permissions revoked. | Guarantees the system can never be locked out. |
| A4 | Permission checks happen **on the server per request** (cached, invalidated on change) rather than being baked into the JWT. | Toggling a permission takes effect immediately, without waiting for token expiry. |
| A5 | Access tokens live 60 minutes; no refresh tokens in this iteration. On expiry the user signs in again. | Keeps scope focused (rubric: avoid over-engineering). Refresh tokens can be added later without API changes to other modules. |
| A6 | Users are deactivated (not soft-deleted) to block access; hard delete is available with a separate permission. | Deactivation is reversible and is the common admin action. |
| A7 | Account lockout: 5 failed sign-ins → 5 minute lockout. Password policy: min 8 chars, upper, lower, digit, non-alphanumeric. | Sensible Identity defaults for an internal CRM. |
| A8 | SQL Server is the production database. Integration tests run on SQLite. | Same stack as other Portal projects; tests stay self-contained. |

## 3. Permission catalogue (this iteration)

| Module | Key | Description |
|---|---|---|
| Users | `Users.View` | View users |
| Users | `Users.Create` | Create users |
| Users | `Users.Edit` | Edit users, activate/deactivate, reset password |
| Users | `Users.Delete` | Delete users |
| Roles | `Roles.View` | View roles and their permissions |
| Roles | `Roles.Create` | Create roles |
| Roles | `Roles.Edit` | Rename / describe roles |
| Roles | `Roles.Delete` | Delete roles |
| Roles | `Roles.ManagePermissions` | Grant / revoke permissions on a role |

Later modules (Customers, Tickets, Dashboard…) add their own entries; the UI picks them up automatically.

## 4. Business rules

| # | Rule | Error |
|---|---|---|
| R1 | Email is unique (case-insensitive). | 409 Conflict |
| R2 | Role name is unique (case-insensitive). | 409 Conflict |
| R3 | A user cannot delete, deactivate, or change the role of **their own** account. | 422 Business rule |
| R4 | The **last active Administrator** cannot be deleted, deactivated or moved to another role. | 422 Business rule |
| R5 | A system role cannot be renamed, deleted, or have its permissions changed. | 422 Business rule |
| R6 | A role that still has users cannot be deleted. | 409 Conflict |
| R7 | Inactive or locked-out users cannot sign in; an inactive user's existing token loses all permissions immediately. | 401 / 403 |
| R8 | Unknown permission ids in a grant/revoke request are rejected. | 400 Validation |

## 5. API contract

All responses are JSON. Errors use RFC 7807 `ProblemDetails` (`errors` dictionary for validation failures).

| Method | Route | Permission | Notes |
|---|---|---|---|
| POST | `/api/auth/login` | anonymous | `{ email, password }` → `{ accessToken, expiresAt, user }` |
| GET | `/api/auth/me` | authenticated | Current profile + effective permissions |
| POST | `/api/auth/change-password` | authenticated | `{ currentPassword, newPassword }` |
| GET | `/api/users` | Users.View | `search, roleId, isActive, page, pageSize, sortBy, sortDirection` → paged |
| GET | `/api/users/{id}` | Users.View | |
| POST | `/api/users` | Users.Create | `{ firstName, lastName, email, phoneNumber, password, roleId, isActive }` |
| PUT | `/api/users/{id}` | Users.Edit | `{ firstName, lastName, email, phoneNumber, roleId, isActive }` |
| PATCH | `/api/users/{id}/status` | Users.Edit | `{ isActive }` |
| POST | `/api/users/{id}/reset-password` | Users.Edit | `{ newPassword }` |
| DELETE | `/api/users/{id}` | Users.Delete | |
| GET | `/api/roles` | Roles.View | `search` → list with user & permission counts |
| GET | `/api/roles/lookup` | Users.View or Roles.View | `{ id, name }` for dropdowns |
| GET | `/api/roles/{id}` | Roles.View | |
| POST | `/api/roles` | Roles.Create | `{ name, description, permissionIds[] }` |
| PUT | `/api/roles/{id}` | Roles.Edit | `{ name, description }` |
| DELETE | `/api/roles/{id}` | Roles.Delete | |
| GET | `/api/roles/{id}/permissions` | Roles.View | Permissions grouped by module with `isGranted` |
| PUT | `/api/roles/{id}/permissions` | Roles.ManagePermissions | `{ permissionIds[], isGranted }` — one toggle or a whole module |
| GET | `/api/permissions` | Roles.View | Full catalogue grouped by module |

## 6. Acceptance criteria

**Authentication**
- [x] AC1 — Valid credentials return a JWT and the user's role and permissions.
- [x] AC2 — Wrong password returns 401 with a generic message (no user enumeration).
- [x] AC3 — Inactive users cannot sign in.
- [x] AC4 — Requests without a token to protected endpoints return 401.

**Authorization**
- [x] AC5 — A signed-in user without the required permission receives 403.
- [x] AC6 — Granting a permission to a role makes the endpoint accessible **on the next request** (no re-login).
- [x] AC7 — Revoking it makes the endpoint return 403 on the next request.
- [x] AC8 — The UI hides menu items, pages, and buttons the user has no permission for, and guards routes.

**Users**
- [x] AC9 — Admin can list (search, filter by role/status, sort, paginate), create, edit, activate/deactivate, reset password, delete.
- [x] AC10 — Invalid input returns 400 with per-field errors, shown next to the fields in the UI.
- [x] AC11 — Duplicate email returns 409.
- [x] AC12 — Rules R3 and R4 are enforced.

**Roles & permissions**
- [x] AC13 — Admin can create, rename and delete roles (R2, R5, R6 enforced).
- [x] AC14 — The permissions screen lists every permission from the catalogue grouped by module, each with a toggle; a module header toggle grants/revokes the whole module.
- [x] AC15 — The Administrator role is shown read-only with all permissions on.

**UX**
- [x] AC16 — All pages share one design system (colors, font family, font sizes, spacing, components).
- [x] AC17 — Every mutation gives feedback (toast); destructive actions ask for confirmation; loading and empty states are shown.
