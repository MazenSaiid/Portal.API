# Plan 006 — Audit Logs: approach and tasks

Spec: [006-audit-logs.md](../specs/006-audit-logs.md)

## Approach

* **`AuditTrail` (Infrastructure)** inspects the change tracker before saving: it collects Added/Modified/Deleted
  entries of audited types, with property values minus excluded fields. After the save (when generated ids exist) it
  writes `AuditLog` rows, and both saves run inside one transaction.
* **Readable summaries** — each audited type has a small label function (customer → "Al Noor Trading (CUS-00003)").
  Join rows (role permissions, user roles) resolve role/permission/user names first.
* **`IAuditLogger` (Application)** for security events that aren't data changes; used by `AuthService` and `UserService`.
* **`ICurrentUser`** gains `UserName` (from the JWT `name` claim) and `IpAddress`, so the context never has to query users.
* **Append-only guard** — `SaveChanges` throws if an `AuditLog` row is modified or deleted.
* **Frontend** — an *Audit log* page under Administration with filters and expandable change details.

## Tasks

| # | Task | Done |
|---|---|---|
| T1 | Spec, assumptions, acceptance criteria | ✅ |
| T2 | Domain: AuditLog, actions, permission | ✅ |
| T3 | Infrastructure: AuditTrail capture, transaction, append-only guard, migration | ✅ |
| T4 | Application: IAuditLogger, security events in Auth/User services, AuditLogService queries | ✅ |
| T5 | API: AuditLogsController; CurrentUser name + IP | ✅ |
| T6 | Tests for AL1–AL8 | ✅ |
| T7 | Frontend: audit log page with filters, change details, navigation | ✅ |
| T8 | Frontend tests, walkthrough, docs | ✅ |
