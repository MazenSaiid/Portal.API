# Plan 007 — SLA & Automation: approach and tasks

Spec: [007-sla-automation.md](../specs/007-sla-automation.md)

## Approach

* **`SlaCalculator` (Domain, pure)** — due dates from policy + creation time, and the state rules of S5. It is unit
  tested with fixed clocks, and the API DTOs and the rule engine use the same code.
* **Ticket fields** — `FirstResponseDueAt`, `ResolutionDueAt`, `FirstRespondedAt`. They are set in `TicketService.Create`,
  recomputed on priority change, and `FirstRespondedAt` is stamped by the workflow service on the first comment/status change.
* **`AutoAssigner`** — used by ticket creation when the setting is on. It picks the least-loaded agent through the existing
  `Agents()` query.
* **`SlaEngine.RunAsync(now)`** — evaluates active rules against active tickets and records `EscalationRuleExecution`
  (rule, ticket) so each rule fires once. `SlaMonitor` (a hosted service) calls it on a timer; tests call it directly
  with a chosen `now`, so no waiting and no flaky timing.
* **`INotifier`** — writes `Notification` rows; used by workflow (assignment, escalation) and the engine.
* **Frontend** — an SLA & automation admin page (targets, auto-assign switch, rules), an SLA badge component used in
  the ticket list, ticket page and dashboard, and a notifications bell with a dropdown.

## Tasks

| # | Task | Done |
|---|---|---|
| T1 | Spec, assumptions, rules, acceptance criteria | ✅ |
| T2 | Domain: SlaPolicy, AutomationSettings, EscalationRule, EscalationRuleExecution, Notification, SlaCalculator, ticket SLA fields, permission | ✅ |
| T3 | Infrastructure: configuration, migration, seeding of default targets, hosted `SlaMonitor` | ✅ |
| T4 | Application: SlaService (policies, settings, rules), AutoAssigner, SlaEngine, Notifier + NotificationService, ticket SLA wiring | ✅ |
| T5 | API: SlaController, NotificationsController, SLA in ticket DTOs, `sla` list filter | ✅ |
| T6 | Tests: calculator unit tests; integration tests for SA1–SA7 | ✅ |
| T7 | Frontend: SLA admin page, SLA badge (list, ticket page, dashboard), notifications bell | ✅ |
| T8 | Frontend tests, browser walkthrough (with audit log), docs | ✅ |
