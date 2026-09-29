# Plan 004 — Ticket Management: approach and tasks

Spec: [004-ticket-management.md](../specs/004-ticket-management.md)

## Approach

* **Workflow in the domain** — `TicketWorkflow` (Domain) holds the transition table, so the API, the tests and the
  `allowedStatuses` field all use one definition of W1.
* **History as a side effect of every change** — the services write `TicketHistoryEntry` rows in the same
  `SaveChanges` as the change itself. Names (assignee, category) are stored as text snapshots, so history reads
  correctly even after a user or category is renamed or deleted.
* **Two services** — `TicketService` (query, create, edit, delete) and `TicketWorkflowService` (status, assignment,
  escalation, comments), plus a small `TicketCategoryService`.
* **Agents from permissions** — assignees are active users whose role grants `Tickets.Work`, found with one query
  over `RolePermissions`.
* **Frontend** — tickets list, new-ticket dialog with a customer picker, a ticket page (description, timeline with
  comment box, properties side panel with status/assign/escalate actions), a categories page, and a *Tickets* tab
  on the customer page.

## Tasks

| # | Task | Done |
|---|---|---|
| T1 | Spec, workflow, rules, acceptance criteria | ✅ |
| T2 | Domain: Ticket, TicketCategory, TicketHistoryEntry, enums, `TicketWorkflow`, permissions | ⬜ |
| T3 | Infrastructure: EF configuration, migration, category seeding; block deleting customers with tickets | ⬜ |
| T4 | Application: DTOs, validators, TicketService, TicketWorkflowService, TicketCategoryService, customer lookup | ⬜ |
| T5 | API: TicketsController, TicketCategoriesController, customer lookup endpoint | ⬜ |
| T6 | Tests: workflow unit tests, integration tests for TK1–TK9 | ⬜ |
| T7 | Frontend: tickets API, list page with filters, new ticket dialog with customer picker | ⬜ |
| T8 | Frontend: ticket page (timeline, comments, status, assign, escalate, edit), categories page, customer Tickets tab | ⬜ |
| T9 | Frontend tests, browser walkthrough, docs | ⬜ |
