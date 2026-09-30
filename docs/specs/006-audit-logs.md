# Spec 006 — Audit Logs

| Item | Value |
|---|---|
| Source module | PDF §10 *Security & Administration* — audit logs |
| Rubric scope | Supports *Authentication and authorization* and *Testing, Security & Edge Cases* |
| Status | Implemented — awaiting sign-off |

## 1. Goal

Administrators can answer "who changed what, when, and from where?" for every important record and security event,
without any developer having to remember to write a log line.

## 2. Assumptions

| # | Assumption | Why |
|---|---|---|
| L1 | Data changes are captured **automatically** in `AppDbContext.SaveChanges`, in the **same transaction** as the change. | A change and its audit entry commit or fail together, and new features are audited for free. |
| L2 | Audited: users, role assignments, roles, role permissions, customers (and their contacts, interactions, notes, files), tickets, ticket categories, quick replies. **Not** audited: private tasks, refresh tokens, ticket history (already a per-ticket audit), the synced permission catalogue, and the audit log itself. | Covers business and security data without noise or duplication. |
| L3 | Each entry stores the user id **and a name snapshot**, the IP address, the action, the entity type and id, a readable summary, and the changed fields as JSON (`field`, `from`, `to`). | Stays readable after users are renamed or deleted. |
| L4 | **Secrets never enter the log**: password hashes, security/concurrency stamps, file storage keys and normalised copies of fields are excluded. Long text is cut to 500 characters. | The log must not become a leak. |
| L5 | **Security events** are logged explicitly: sign-in, failed sign-in, lockout, sign-out, password change, admin password reset, refresh-token reuse. Failed sign-ins record the attempted email. | These aren't plain data changes. |
| L6 | The log is **append-only**: there are no update/delete endpoints, and the DbContext refuses to modify or delete audit rows. | Tamper resistance at the application level. |
| L7 | Retention/archiving is out of scope for now; the table is indexed by time, user and entity for fast filtering. | Keep scope focused. |

## 3. Actions

`Created`, `Updated`, `Deleted` (data) · `SignedIn`, `SignInFailed`, `LockedOut`, `SignedOut`, `PasswordChanged`,
`PasswordReset`, `TokenReuseDetected` (security).

## 4. API

| Method | Route | Permission |
|---|---|---|
| GET | `/api/audit-logs?search&action&entityType&entityId&userId&from&to&page&pageSize` | AuditLogs.View |
| GET | `/api/audit-logs/entity-types` | AuditLogs.View |

New permission: `AuditLogs.View` — *View the audit log*.

## 5. Acceptance criteria

- [ ] AL1 — Creating, updating and deleting a customer, ticket, role, user or category produces entries with the user, time, IP, summary and changed fields (from → to).
- [ ] AL2 — Granting/revoking a permission and changing a user's role are logged with readable names.
- [ ] AL3 — Sign-in, failed sign-in (with attempted email), lockout, sign-out, password change/reset and token reuse are logged.
- [ ] AL4 — Password hashes, stamps and storage keys never appear in the log.
- [ ] AL5 — Updates that only touch noise fields (e.g. last sign-in time) produce no data entry.
- [ ] AL6 — The log can be filtered by text, action, entity type/id, user and date range, newest first, paged.
- [ ] AL7 — Audit rows can't be changed or deleted through the application.
- [ ] AL8 — Only users with `AuditLogs.View` can read the log; the UI shows the changed fields per entry.
