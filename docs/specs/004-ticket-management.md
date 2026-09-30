# Spec 004 — Ticket Management

| Item | Value |
|---|---|
| Area | *Ticket Management* — create and track tickets, categories and priorities, assign to agents, status and escalation, ticket history |
| Status | Implemented |

## 1. Goal

Every customer request becomes a ticket that moves through a clear workflow. Someone always owns it, urgent work
stands out, and every change is recorded so anyone can see what happened and who did it.

## 2. Assumptions

| # | Assumption | Why |
|---|---|---|
| K1 | A ticket belongs to exactly one **customer** (Spec 003) and gets a readable code `TCK-00042`. | Agents quote codes on calls and emails. |
| K2 | **Categories** are data managed by admins (seeded: General, Billing, Technical, Account, Complaint). A category in use cannot be deleted, only deactivated; inactive categories can't be chosen for new tickets. | Keeps history intact while letting the list evolve. |
| K3 | **Priorities** are fixed: Low, Medium, High, Urgent. **Statuses** are fixed: New, Open, In progress, On hold, Resolved, Closed. | The workflow rules below depend on them; making them data would make the rules unenforceable. |
| K4 | An **agent** is an active user whose role has `Tickets.Work`. Only agents can be assignees. | "Assign tickets to agents" follows from permissions, with no separate agent flag. |
| K5 | **History is automatic and immutable**: creation, field changes, status changes, assignment, escalation and comments are all written by the server. | A trustworthy audit trail per ticket. |
| K6 | Comments are internal team notes for now. Customer-visible replies arrive with the Communication Channels / Customer Portal modules. | Keeps scope focused. |
| K7 | A customer with tickets cannot be deleted (closes the note in Spec 003, C8). | Tickets must never lose their customer. |

## 3. Workflow

```
            ┌────────── reopen ──────────┐
            ▼                            │
 New ──► Open ◄──► In progress ◄──► On hold
  │        │            │              │
  └────────┴────────────┴──────────────┴──► Resolved ──► Closed
                                              │  ▲         │
                                              └──┴─reopen──┘ (→ Open)
```

| # | Rule | Error |
|---|---|---|
| W1 | Only the transitions above are allowed. The API returns `allowedStatuses` for the current state, so the UI offers only valid moves. | 422 |
| W2 | *In progress* requires an assignee. | 422 |
| W3 | *Resolved* requires a resolution comment. | 400 |
| W4 | Assigning a *New* ticket moves it to *Open*. | — |
| W5 | *Closed* tickets are read-only (no edits, comments, assignment or escalation) until reopened. | 422 |
| W6 | Resolving sets `ResolvedAt`, closing sets `ClosedAt`, and reopening clears both. | — |

## 4. Assignment & escalation

| # | Rule | Error |
|---|---|---|
| A1 | Holders of `Tickets.Assign` can assign any agent or unassign. | — |
| A2 | Holders of only `Tickets.Work` can take an unassigned ticket for themselves ("Assign to me") or release their own. | 403 otherwise |
| A3 | The assignee must be an active agent (K4). | 400 |
| E1 | Escalating needs a reason, marks the ticket escalated and raises its priority to at least *High*. | 400 without a reason |
| E2 | Resolved/closed tickets can't be escalated; an escalated ticket can't be escalated again. | 422 |
| E3 | De-escalating clears the flag and keeps the priority. | — |
| E4 | Resolving or closing an escalated ticket clears the escalation automatically (recorded in history). | — |

## 5. Other rules

| # | Rule | Error |
|---|---|---|
| T1 | Subject (≤ 200) and description (≤ 8000) are required; the category must exist and be active. | 400 |
| T2 | Tickets can't be opened for an inactive customer. | 422 |
| T3 | Category names are unique; a category in use can't be deleted. | 409 |

## 6. Permissions

| Key | Description |
|---|---|
| `Tickets.View` | View tickets and their history |
| `Tickets.Create` | Create tickets |
| `Tickets.Edit` | Edit ticket subject, description, category, priority and channel |
| `Tickets.Work` | Work tickets: change status, comment, and be assigned |
| `Tickets.Assign` | Assign tickets to any agent |
| `Tickets.Escalate` | Escalate and de-escalate tickets |
| `Tickets.Delete` | Delete tickets |
| `Tickets.ManageCategories` | Manage ticket categories |

## 7. API

| Method | Route | Permission |
|---|---|---|
| GET | `/api/tickets?search&status&priority&categoryId&assignedTo(me\|unassigned\|id)&customerId&escalated&page&pageSize&sortBy&sortDirection` | Tickets.View |
| GET | `/api/tickets/{id}` | Tickets.View |
| POST | `/api/tickets` | Tickets.Create |
| PUT | `/api/tickets/{id}` | Tickets.Edit |
| DELETE | `/api/tickets/{id}` | Tickets.Delete |
| POST | `/api/tickets/{id}/status` `{ status, comment }` | Tickets.Work |
| POST | `/api/tickets/{id}/assign` `{ assigneeId }` | Tickets.Assign or Tickets.Work (A2) |
| POST | `/api/tickets/{id}/escalate` `{ reason }` · `/de-escalate` `{ comment }` | Tickets.Escalate |
| POST | `/api/tickets/{id}/comments` `{ content }` | Tickets.Work |
| GET | `/api/tickets/{id}/history` | Tickets.View |
| GET | `/api/tickets/assignees` | Tickets.View |
| GET/POST/PUT/DELETE | `/api/ticket-categories[/{id}]` | View / ManageCategories |
| GET | `/api/customers/lookup?search` | Customers.View or Tickets.Create |

`status` may be repeated (`?status=New&status=Open`) to filter several at once.

## 8. Acceptance criteria

- [x] TK1 — Create a ticket for a customer with category, priority, channel and optional assignee; it is persisted with code, status *New* (or *Open* when assigned) and a *Created* history entry.
- [x] TK2 — The list filters by status (incl. several), priority, category, assignee (me / unassigned / agent), customer and escalation; supports search by subject/code/customer, sorting and paging.
- [x] TK3 — Edit subject, description, category and priority; each change is recorded in history.
- [x] TK4 — The status workflow W1–W6 is enforced; the UI only offers allowed statuses.
- [x] TK5 — Assignment rules A1–A3 are enforced, and history records assignee changes by name.
- [x] TK6 — Escalation rules E1–E3 are enforced and escalated tickets stand out in the list.
- [x] TK7 — Comments are added to the timeline; history shows who did what and when, oldest first.
- [x] TK8 — Categories can be managed (T3); inactive ones aren't offered for new tickets.
- [x] TK9 — Every endpoint is protected by the permissions above; the customer page shows that customer's tickets, and customers with tickets can't be deleted (K7).
