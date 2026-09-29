# Plan 003 — Customer Management: approach and tasks

Spec: [003-customer-management.md](../specs/003-customer-management.md)

## Approach

* **Auditing** — a small `AuditableEntity` base (Domain). `AppDbContext.SaveChanges` stamps `CreatedAt/By` and
  `UpdatedAt/By` from `ICurrentUser`. Audit columns have no FK to Users, so deleting a user never touches history.
  This is also the hook the audit-log module will build on.
* **Customer id** — integer identity, so the display code `CUS-00042` comes free and stays stable.
* **Two services** — `CustomerService` (profile + contacts) and `CustomerActivityService` (interactions, notes,
  attachments). This keeps each file focused.
* **Files** — `IFileStorage` (Application) with `LocalFileStorage` (Infrastructure) under `Storage:RootPath`. The
  service receives a `Stream`, so Application never depends on ASP.NET's `IFormFile`.
* **Enums as strings** in JSON (`JsonStringEnumConverter`) for readable payloads and frontend types.
* **Frontend** — list page + details page with tabs (Activity, Notes, Attachments, Contacts), reusing the shared UI
  kit. New shared pieces: `app-tabs` and a file-size pipe.

## Tasks

| # | Task | Done |
|---|---|---|
| T1 | Spec, assumptions, acceptance criteria | ✅ |
| T2 | Domain: `AuditableEntity`, Customer, Contact, Interaction, Note, Attachment, enums, permissions | ⬜ |
| T3 | Infrastructure: EF configurations, audit stamping, migration, `LocalFileStorage` | ⬜ |
| T4 | Application: DTOs, validators (CR1–CR8), `CustomerService`, `CustomerActivityService` | ⬜ |
| T5 | API: `CustomersController`, multipart upload with size limit, safe download | ⬜ |
| T6 | Integration + unit tests for CM1–CM8 | ⬜ |
| T7 | Frontend: customers API service, list page, customer form dialog | ⬜ |
| T8 | Frontend: details page, tabs, activity timeline, notes, attachments, contacts | ⬜ |
| T9 | Frontend tests, browser walkthrough, docs | ⬜ |
