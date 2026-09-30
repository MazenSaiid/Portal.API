# Spec 005 — Agent Dashboard

| Item | Value |
|---|---|
| Source module | PDF §4 *Agent Dashboard* — assigned tickets, customer information, tasks and reminders, quick replies, team collaboration |
| Rubric scope | **Mandatory** — *Agent dashboard: shows assigned tickets and useful customer/ticket context* |
| Status | Implemented — awaiting sign-off |

## 1. Goal

An agent signs in and immediately sees what to work on next: their tickets in priority order with the customer
behind each one, tickets waiting for someone to take them, their own to-dos and reminders, and what teammates did
on their tickets. Frequent answers are one click away as quick replies.

## 2. Assumptions

| # | Assumption | Why |
|---|---|---|
| D1 | The dashboard is **personal**: everything is computed for the signed-in user. It becomes the landing page for users with `Dashboard.View`; others keep the Overview page. | Agents need "my work", not global statistics (those belong to *Reports*, PDF §9). |
| D2 | **Tasks are private to-dos** of their owner, optionally linked to a ticket and/or customer. Other users can't see them (404, so it's not even revealed that they exist). | Personal reminders like "call back Thursday". |
| D3 | **Reminders** = open tasks that are overdue or due today, surfaced as a badge in the top bar and at the top of the task list. Push/email alerts come with *SLA & Automation* (PDF §5). | Useful now without a notification infrastructure. |
| D4 | **Quick replies** are either **shared** (managed by `QuickReplies.Manage`, visible to all agents) or **personal** (owned by one agent). They support the placeholders `{customer}`, `{agent}` and `{ticket}`, filled in when inserted into a ticket comment. | Consistent wording without retyping. |
| D5 | **Team collaboration** in this iteration means: team workload (active tickets per agent) and a feed of what others did on *my* tickets. @mentions and notifications come with *Alerts & notifications* (PDF §5). | Covers the visibility need without a notification system. |
| D6 | Deleting a ticket or customer unlinks tasks rather than deleting them. | A reminder shouldn't vanish silently. |

## 3. Dashboard content

| Section | Content |
|---|---|
| Summary | My active tickets, in progress, escalated, high/urgent, unassigned queue, resolved by me in the last 7 days |
| My tickets | Active tickets assigned to me, **escalated → priority → last activity**, each with customer name, code, email/phone and the customer's other active tickets |
| Unassigned queue | Up to 5 unassigned active tickets, highest priority and oldest first, with **Take** (if `Tickets.Work`) |
| Team workload | Every agent with their active ticket count |
| Team activity | Last 10 changes/comments made by *others* on tickets assigned to me |
| My tasks | Open tasks, overdue first then by due date; quick add; complete; show completed |

Ticket sections appear only for users with `Tickets.View`.

## 4. Rules

| # | Rule | Error |
|---|---|---|
| DT1 | Task title required (≤ 200), notes ≤ 2000; a linked ticket/customer must exist | 400 |
| DT2 | Only the owner can see, change, complete or delete a task | 404 |
| QR1 | Quick reply title required (≤ 100), body required (≤ 4000) | 400 |
| QR2 | Creating, editing or deleting a **shared** reply needs `QuickReplies.Manage`; personal replies can only be changed by their owner | 403 / 404 |

## 5. Permissions

| Key | Description |
|---|---|
| `Dashboard.View` | Use the agent dashboard, personal tasks and reminders |
| `QuickReplies.Manage` | Manage shared quick replies |

Personal quick replies and using quick replies need `Tickets.Work`.

## 6. API

| Method | Route | Permission |
|---|---|---|
| GET | `/api/dashboard` | Dashboard.View |
| GET | `/api/tasks?status=open\|done\|all` | Dashboard.View |
| GET | `/api/tasks/reminders` → `{ overdue, dueToday }` | Dashboard.View |
| POST / PUT / DELETE | `/api/tasks[/{id}]` | Dashboard.View + DT2 |
| POST | `/api/tasks/{id}/complete` `{ isDone }` | Dashboard.View + DT2 |
| GET | `/api/quick-replies` (shared + mine) | Tickets.Work or QuickReplies.Manage |
| POST / PUT / DELETE | `/api/quick-replies[/{id}]` | Tickets.Work or QuickReplies.Manage + QR2 |

## 7. Acceptance criteria

- [ ] AD1 — After sign-in, a user with `Dashboard.View` lands on the dashboard; the summary numbers match their tickets.
- [ ] AD2 — "My tickets" lists only my active tickets in the order escalated → priority → last activity, with customer context.
- [ ] AD3 — The unassigned queue shows waiting tickets and an agent can take one from the dashboard.
- [ ] AD4 — Team workload and the team-activity feed (others' actions on my tickets) are shown.
- [ ] AD5 — Tasks can be added, edited, completed, reopened and deleted, optionally linked to a ticket; other users can't reach them (DT1, DT2).
- [ ] AD6 — Overdue and due-today tasks show as a reminder badge in the top bar.
- [ ] AD7 — Quick replies (shared + personal) can be managed (QR1, QR2) and inserted into a ticket comment with placeholders filled in.
- [ ] AD8 — Summary tiles link to the matching filtered ticket list.
