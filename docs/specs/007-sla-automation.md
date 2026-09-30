# Spec 007 — SLA & Automation

| Item | Value |
|---|---|
| Area | *SLA & Automation* — response and resolution targets, automatic assignment, escalation rules, alerts and notifications |
| Status | Implemented |

## 1. Goal

Every ticket carries a clear promise ("first response within 2 hours, resolved within 1 day"). The system tracks
it, hands new work to the least-busy agent, escalates automatically when promises are about to break, and tells
the right people in the app.

## 2. Assumptions

| # | Assumption | Why |
|---|---|---|
| S1 | **Targets are per priority** (first response and resolution, in minutes), measured in **calendar time (24/7)** from ticket creation. Business-hours calendars and pausing while *On hold* are out of scope. | Simple, predictable rules; calendars can be added behind the same calculation later. |
| S2 | Defaults: Urgent 30 min / 4 h · High 2 h / 1 day · Medium 8 h / 3 days · Low 1 day / 5 days. Admins can change them. | Common starting points. |
| S3 | **First response** = the first comment or status change after creation, by anyone. | The first sign that someone is working on it. |
| S4 | Due dates are stored on the ticket and **recomputed when the priority changes** (still from the creation time). Already-met targets stay met. Changing the targets themselves affects new tickets and later priority changes; existing promises are kept. Tickets created before SLA tracking get due dates once, at startup. | A priority change changes the promise; a policy change shouldn't silently break existing ones. |
| S5 | SLA state per target: *Met*, *Breached* (met late, or overdue), *At risk* (≥ 75 % of the time used), *On track*; resolved/closed tickets stop the resolution clock. | One consistent traffic light in lists, the ticket page and the dashboard. |
| S6 | **Auto-assignment** (off by default): a new unassigned ticket goes to the active agent (K4) with the fewest active tickets; ties go to whoever was assigned least recently. | Fair load balancing without manual triage. |
| S7 | **Escalation rules** are data: *trigger* (first response breached, resolution at risk, resolution breached, unassigned longer than N minutes) + optional *minimum priority* + *actions* (escalate, raise priority to X, notify assignee and/or supervisors). Each rule fires **at most once per ticket**. | Admins shape automation without code; no alert storms. |
| S8 | A background job evaluates rules every minute (configurable). Rules only look at active tickets. | Near-real-time without extra infrastructure. |
| S9 | **Notifications are in-app**: a bell with an unread count and a list. Delivery by email, SMS or WhatsApp is not part of the current scope. *Supervisors* = active users holding `Tickets.Assign`. | Useful now, easy to extend with delivery channels. |
| S10 | Users are notified when a ticket is assigned to them by someone else (or by auto-assignment), when their ticket is escalated, and by rule actions. | The events an agent must not miss. |

## 3. Rules

| # | Rule | Error |
|---|---|---|
| SL1 | Targets must be positive, at most 60 days, and resolution ≥ first response | 400 |
| SL2 | Rule name required (≤ 100); *unassigned for* needs threshold minutes ≥ 1; a rule needs at least one action | 400 |
| SL3 | "Raise priority to" never lowers a priority | — |
| SL4 | A user can only read and mark their own notifications | 404 |

## 4. Permissions

| Key | Description |
|---|---|
| `Sla.Manage` | Manage SLA targets, auto-assignment and escalation rules |

Notifications need only a signed-in user; SLA information on tickets follows `Tickets.View`.

## 5. API

| Method | Route | Permission |
|---|---|---|
| GET / PUT | `/api/sla/policies` | Tickets.View / Sla.Manage |
| GET / PUT | `/api/sla/settings` `{ autoAssignEnabled }` | Sla.Manage |
| GET / POST / PUT / DELETE | `/api/sla/rules[/{id}]` | Sla.Manage |
| GET | `/api/notifications?unreadOnly` · `/api/notifications/unread-count` | signed in |
| POST | `/api/notifications/{id}/read` · `/api/notifications/read-all` | signed in |

Ticket DTOs gain `sla: { firstResponseDueAt, firstRespondedAt, firstResponseState, resolutionDueAt, resolutionState }`,
and the ticket list can filter `?sla=breached|atRisk`.

## 6. Acceptance criteria

- [x] SA1 — New tickets get first-response and resolution due dates from their priority's targets; a priority change recomputes them.
- [x] SA2 — The first comment or status change records the first response; SLA states are computed as S5.
- [x] SA3 — Admins can edit targets (SL1) and toggle auto-assignment.
- [x] SA4 — With auto-assignment on, a new unassigned ticket goes to the least-loaded active agent and the history says so.
- [x] SA5 — Escalation rules can be managed (SL2) and, when evaluated, escalate / raise priority (SL3) / notify, once per ticket.
- [x] SA6 — Users get notifications for assignment, escalation of their ticket, and rule alerts; they can read them and mark them as read (SL4).
- [x] SA7 — Tickets list, ticket page and dashboard show SLA status; the list can filter breached / at-risk tickets.
