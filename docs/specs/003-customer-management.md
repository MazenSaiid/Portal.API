# Spec 003 — Customer Management

| Item | Value |
|---|---|
| Source module | PDF §1 *Customer Management* — profiles, contact details, interaction history, notes and attachments |
| Rubric scope | **Mandatory** — *Customer CRUD with validation* |
| Status | Implemented — awaiting sign-off |

## 1. Goal

Agents get one place per customer: who they are, how to reach them, what happened with them before,
and the documents and notes the team keeps. Later modules (tickets, channels, portal) attach to this customer record.

## 2. Assumptions

| # | Assumption | Why |
|---|---|---|
| C1 | A customer is an **Individual** or a **Company**. A company can have several **contact persons**; one of them may be primary. | B2B support needs to know who to call at a company. |
| C2 | Customers have a readable code `CUS-00042` derived from an integer id. | Agents read codes over the phone; GUIDs are unusable there. |
| C3 | A customer needs **at least one way to reach them** (email or phone). The **preferred channel** must be one they have: Email needs an email; Phone/WhatsApp/SMS need a phone. | Prevents records nobody can contact. |
| C4 | Customer email is unique (case-insensitive) when present. | Stops duplicate records; tickets from an email must map to one customer. |
| C5 | **Interactions are history and are immutable**: they can be logged but not edited or deleted. | Keeps the record trustworthy. Mistakes are corrected by logging a follow-up. |
| C6 | Notes and attachments can be edited or removed by **their author**, or by anyone with `Customers.Edit`. | Team members own their notes; supervisors can clean up. |
| C7 | Attachments: max **10 MB**, allowed types pdf, png, jpg/jpeg, txt, csv, doc/docx, xls/xlsx. Files are stored on disk under random names and always downloaded as attachments. | Blocks executable and HTML uploads. Original names are never used as paths. |
| C8 | Deleting a customer deletes their contacts, interactions, notes and attachment files. Deactivation is the reversible option. When tickets arrive, customers with tickets will be protected from deletion. | Simple for now, with a clear path. |
| C9 | All records carry `CreatedAt/By` and `UpdatedAt/By`, set automatically. | Accountability, and the base for the audit-log module. |

## 3. Permissions

| Key | Description |
|---|---|
| `Customers.View` | View customers, their contacts, interactions, notes and attachments |
| `Customers.Create` | Create customers |
| `Customers.Edit` | Edit customer profiles and contacts; manage anyone's notes/attachments |
| `Customers.Delete` | Delete customers |
| `Customers.AddActivity` | Log interactions, add notes, upload attachments |

The permissions screen picks these up automatically (Spec 001, A2).

## 4. Business rules

| # | Rule | Error |
|---|---|---|
| CR1 | Name required; email/phone valid when given; at least one of them present (C3) | 400 |
| CR2 | Preferred channel must be reachable with the given details (C3) | 400 on `preferredChannel` |
| CR3 | Duplicate customer email | 409 |
| CR4 | Marking a contact primary un-marks the previous primary | — |
| CR5 | Contact needs a name and an email or phone | 400 |
| CR6 | Interaction `occurredAt` cannot be in the future (5 min tolerance) | 400 |
| CR7 | Only the author, or a `Customers.Edit` holder, may edit or delete a note or attachment | 403 |
| CR8 | Attachment too large, empty, or of a disallowed type | 400 |

## 5. API

| Method | Route | Permission |
|---|---|---|
| GET | `/api/customers?search&type&isActive&page&pageSize&sortBy&sortDirection` | Customers.View |
| GET | `/api/customers/{id}` | Customers.View |
| POST | `/api/customers` | Customers.Create |
| PUT | `/api/customers/{id}` | Customers.Edit |
| DELETE | `/api/customers/{id}` | Customers.Delete |
| POST / PUT / DELETE | `/api/customers/{id}/contacts[/{contactId}]` | Customers.Edit |
| GET | `/api/customers/{id}/interactions?page&pageSize` | Customers.View |
| POST | `/api/customers/{id}/interactions` | Customers.AddActivity |
| GET / POST | `/api/customers/{id}/notes` | View / AddActivity |
| PUT / DELETE | `/api/customers/{id}/notes/{noteId}` | AddActivity + CR7 |
| GET / POST (multipart) | `/api/customers/{id}/attachments` | View / AddActivity |
| GET | `/api/customers/{id}/attachments/{attachmentId}/download` | Customers.View |
| DELETE | `/api/customers/{id}/attachments/{attachmentId}` | AddActivity + CR7 |

Search matches name, email, phone and code (`CUS-00042` or `42`). Enums travel as strings.

## 6. Acceptance criteria

- [x] CM1 — Create, view, edit and delete a customer. Invalid data returns per-field errors shown next to the fields (CR1, CR2).
- [x] CM2 — Duplicate email returns 409 (CR3).
- [x] CM3 — The list supports search (including by code), type and status filters, sorting and paging, and shows the last interaction date.
- [x] CM4 — Contacts can be added, edited and removed, with a single primary contact (CR4, CR5).
- [x] CM5 — Interactions are logged with type, direction, subject, summary and time. They show newest first and cannot be edited (C5, CR6).
- [x] CM6 — Notes can be added. Only the author or an editor can change or delete them (CR7).
- [x] CM7 — Files can be uploaded, downloaded and deleted within the limits (CR7, CR8).
- [x] CM8 — Every endpoint is protected by the permissions above; users without `Customers.AddActivity` see a read-only profile.
- [x] CM9 — Pages use the approved design system.
