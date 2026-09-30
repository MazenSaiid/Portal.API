# Plan 005 — Agent Dashboard: approach and tasks

Spec: [005-agent-dashboard.md](../specs/005-agent-dashboard.md)

## Approach

* **One read endpoint** — `GET /api/dashboard` returns every section in one response. The dashboard is a read model
  built from existing tables (tickets, history, tasks), so no data is duplicated.
* **Tasks** — `AgentTask` (owner, title, notes, due date, done, optional ticket/customer link). All queries filter by
  the current user, so DT2 can't be missed. Ticket/customer FKs are `ClientSetNull`: SQL Server forbids a second
  cascade path from Users, and the ticket/customer delete code unlinks tasks first (D6).
* **Quick replies** — `QuickReply` with a nullable owner (null = shared). Seeded with three shared replies.
  Placeholders are filled on the client, where the ticket and customer are already loaded.
* **Frontend** — the dashboard page; a shared task dialog also opened from the ticket page ("Add reminder"); a
  quick-replies page; a quick-reply picker in the ticket comment box; a reminder badge in the top bar; and a ticket
  list that reads its filters from the URL so summary tiles can deep-link.

## Tasks

| # | Task | Done |
|---|---|---|
| T1 | Spec, assumptions, rules, acceptance criteria | ✅ |
| T2 | Domain: AgentTask, QuickReply, permissions | ✅ |
| T3 | Infrastructure: EF configuration, migration, quick-reply seeding, unlink tasks on delete | ✅ |
| T4 | Application: DashboardService, TaskService, QuickReplyService + validators | ✅ |
| T5 | API: DashboardController, TasksController, QuickRepliesController | ✅ |
| T6 | Tests: integration tests for AD1–AD7, unit tests for rules | ✅ |
| T7 | Frontend: dashboard page, task dialog, landing redirect, reminder badge | ✅ |
| T8 | Frontend: quick-replies page, picker in the ticket comment box, "Add reminder" on the ticket page, URL filters | ✅ |
| T9 | Frontend tests, browser walkthrough, docs | ✅ |
