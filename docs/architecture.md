# How Portal works

This document explains **what happens, step by step**, when people use Portal, and then shows how the system is
built to make that happen. Part 1 is for everyone. Part 2 adds the technical design for developers.

---

# Part 1 — How things happen

## 1. The big picture

```
   Support staff                     Portal
 ┌──────────────┐    requests    ┌─────────────────────┐    reads/writes   ┌──────────────┐
 │  Web browser │ ─────────────▶ │  Portal API          │ ───────────────▶ │  SQL Server   │
 │  (Angular)   │ ◀───────────── │  checks who you are, │ ◀─────────────── │  database     │
 └──────────────┘    answers     │  what you may do,    │                  └──────────────┘
                                 │  and the rules       │ ──▶ uploaded files (disk)
                                 └─────────────────────┘
                                          ▲
                                          │ every minute
                                 ┌─────────────────────┐
                                 │  SLA monitor         │  checks deadlines, escalates, notifies
                                 └─────────────────────┘
```

* The **web application** is what people see: pages, forms and buttons. It never decides anything important on
  its own. It hides buttons you can't use, but the real decision is always made by the API.
* The **API** is the gatekeeper and the brain. Every request is checked for *who you are*, *whether you're
  allowed*, and *whether it follows the business rules*, before anything is saved.
* The **database** keeps everything: users, customers, tickets, history and the audit log.
* The **SLA monitor** runs in the background every minute. It looks for tickets that are about to break or have
  broken their deadlines and applies the escalation rules.

## 2. Who uses Portal

| Person | Typical permissions | What they do all day |
|---|---|---|
| **Agent** | View tickets and customers, work tickets, add activity, dashboard | Picks up tickets, talks to customers, logs what happened, resolves requests |
| **Supervisor** | Everything an agent has, plus assign and escalate tickets | Balances the workload, handles escalations, watches deadlines |
| **Administrator** | Everything | Manages users, roles, categories, SLA targets and automation, reads the audit log |

These are examples. Roles are fully configurable: an administrator can create any role and switch each of its
26 permissions on or off.

## 3. Signing in and staying signed in

1. The user enters their email and password on the sign-in page.
2. The API checks the password.
   * If it's wrong, the answer is always *"Invalid email or password"*, so strangers can't find out which emails exist.
   * After **5 wrong attempts** the account is locked for **5 minutes**.
   * A deactivated account is refused, but only after a correct password, so account status isn't revealed to strangers.
3. On success the API returns two keys:
   * a short **access token** that works for 15 minutes and is sent with every request;
   * a **renewal token** that works for 7 days and is used only to get a new access token.
4. One minute before the access token expires, the web application quietly swaps the renewal token for a new
   pair. The user notices nothing. Each renewal token can be used **once**. If an old one is ever used again,
   Portal assumes it was stolen and signs that user out everywhere.
5. Signing out, being deactivated, or having a password reset by an administrator ends the sessions immediately.
6. Every one of these events (sign-in, failure, lockout, sign-out, password change) goes into the audit log.

## 4. How Portal decides what you may do

1. Every API endpoint is labelled with the permission it needs, for example *"Tickets – Work"* to change a ticket's status.
2. For every request, the API looks up the user's role and that role's permissions. The result is kept in memory
   for speed and cleared the moment an administrator changes anything.
3. Permission is there → the request continues. Missing → the answer is *403 Forbidden*, and the web application
   refreshes its menu so it matches.
4. Because the check happens on **every request**, switching a permission off takes effect on the user's very next
   click, without signing them out.
5. Safety nets that can never be switched off:
   * The **Administrator** role always has every permission and can't be renamed or deleted.
   * Nobody can delete, deactivate or change the role of **their own** account.
   * The **last active administrator** can't be removed or deactivated.

## 5. A customer asks for help: the life of a ticket

**Step 1 — The customer is on file.** An agent searches for the customer by name, email, phone or code, or adds
them. A customer needs at least an email or a phone number, and their preferred channel must be one we can
actually use (WhatsApp needs a phone number, for example).

**Step 2 — A ticket is created.** The agent picks the customer and enters a subject, description, category,
priority and the channel the request came in on. At that moment Portal:
1. gives the ticket its code, e.g. `TCK-00042`, and status **New**;
2. sets its **deadlines** from the SLA targets for its priority, e.g. an Urgent ticket must get a first
   response within 30 minutes and be resolved within 4 hours;
3. if **automatic assignment** is on and no one was chosen, gives it to the agent with the fewest active tickets,
   and it becomes **Open**;
4. **notifies** the assignee (unless they assigned it to themselves);
5. writes *"created"* and *"assigned"* lines on the ticket's timeline, and a *Created* entry in the audit log.

**Step 3 — Someone owns it.** An unassigned ticket waits in the **queue** on every agent's dashboard. An agent
presses *Take*, or a supervisor assigns it. Assigning a *New* ticket moves it to *Open* automatically.

**Step 4 — Work starts.** The agent moves the ticket to **In progress**. This is only allowed once someone is
assigned. The first comment or status change counts as the **first response**, so the first-response deadline is
now *met* or *missed*.

**Step 5 — Keeping track.** The agent adds comments, e.g. *"called procurement, quote comes today"*, and can put
the ticket **On hold** while waiting for someone. Every step shows on the timeline with who and when. With one
click the agent can use a **quick reply** or add a **reminder** for themselves.

**Step 6 — When it gets urgent.** Anyone with the right permission can **escalate** with a reason. The ticket gets
a red flag, its priority rises to at least *High*, and its deadlines tighten to match. The assignee is notified.
Escalation can also happen **automatically** (see section 7).

**Step 7 — Resolved.** The agent moves the ticket to **Resolved** and must write how it was resolved. The
resolution deadline is now *met* or *missed*, and any escalation is cleared automatically.

**Step 8 — Closed, or reopened.** A resolved ticket is **Closed** when done. Closed tickets are read-only. If the
customer comes back, the ticket is **Reopened** and continues where it left off.

```
 New ──▶ Open ◀──▶ In progress ◀──▶ On hold
  │        │            │              │
  └────────┴────────────┴──────────────┴──▶ Resolved ──▶ Closed
                                              ▲   │         │
                                              └───┴─ Reopen ┘ (back to Open)
```

## 6. An agent's day

1. After signing in, the agent lands on the **dashboard**:
   * summary tiles: my active tickets, in progress, escalated, high & urgent, the unassigned queue, resolved this week;
   * **my tickets**: escalated first, then by priority, each with the customer's name and phone and whether
     they have other open tickets;
   * the **unassigned queue**, most urgent and longest-waiting first;
   * **my tasks**, overdue first;
   * **team activity** (what others did on my tickets) and **team workload**.
2. Each tile opens the ticket list already filtered, e.g. *Escalated* shows my escalated tickets.
3. The **calendar badge** in the top bar counts tasks that are overdue or due today. The **bell** shows new
   notifications: a ticket assigned to me, my ticket escalated, or an SLA alert.
4. Tasks are private: nobody else can see or change them.

## 7. Automation: what the SLA monitor does every minute

1. It loads the active escalation rules, for example:
   * *"When the resolution target is missed → escalate and notify supervisors"*;
   * *"When a ticket is unassigned for 30 minutes and is at least Medium → raise it to High"*.
2. For every ticket that still needs work, it checks each rule's condition: first response missed, resolution at
   risk (75 % of the time used), resolution missed, or unassigned for too long. A rule can also be limited to a
   minimum priority.
3. When a rule matches, it runs the rule's actions: escalate, raise the priority (it never lowers one), notify the
   assignee, and/or notify supervisors (everyone who can assign tickets).
4. It remembers that this rule has acted on this ticket, so the same alert is **never sent twice**.
5. Everything it does appears on the ticket's timeline as done by *System*, and in the audit log.

## 8. Customers: the full picture in one place

The customer page brings everything about a customer together, on tabs:
* **Tickets**: every request from this customer, with a button to open a new one.
* **Interactions**: the history of calls, emails, meetings and chats. Entries can't be edited later, because history must be trustworthy.
* **Notes**: internal notes. Only the author, or someone allowed to edit customers, can change or delete one.
* **Files**: contracts, invoices and screenshots, up to 10 MB, of safe types only (PDF, images, Office, text).
* **Contacts**: the people we talk to at a company, with one primary contact.

A customer who still has tickets can't be deleted. Deactivating them keeps the history.

## 9. Every change leaves a trace: the audit log

1. Whenever anything important is saved (a user, role, permission, customer, ticket, category, quick reply or
   SLA setting), Portal writes an audit entry **in the same database transaction**. The change and its record are
   saved together or not at all.
2. Each entry says **who** (name kept even if the user is later deleted), **when**, **from which IP address**,
   **what** in plain words (e.g. *"Customer Al Noor Trading (CUS-00003) updated"*) and the **changed fields with
   before → after**. Linked records are shown by name, not by internal number.
3. Passwords, security stamps and file storage keys are **never** written to the log. Changes that only touch
   internal bookkeeping (like the last sign-in time) don't create entries.
4. Nobody can edit or delete audit entries through the application. The software itself refuses.

---

# Part 2 — Technical design

## 10. Building blocks

```
 Angular web app ──HTTPS / JSON + bearer token──▶ Portal.API ──▶ Portal.Application ──▶ Portal.Domain
                                                  (endpoints,      (use cases,            (entities, workflow,
                                                   permissions,     validation,            SLA arithmetic,
                                                   error answers)   business rules)        permission list)
                                                       │                  ▲
                                                       ▼                  │ interfaces
                                                Portal.Infrastructure ────┘
                                                (SQL Server via EF Core, Identity, tokens, file storage,
                                                 audit trail, SLA monitor, starter data)
```

| Layer | Responsibility | Knows about |
|---|---|---|
| **Domain** | Business vocabulary and pure rules: entities, the ticket workflow table, SLA calculations, the permission catalogue | Nothing else |
| **Application** | Use cases: one service per area (customers, tickets, dashboard…), input validation, business rules, error types | Domain + interfaces it defines |
| **Infrastructure** | The database, sign-in and passwords, token creation, file storage, the audit trail, the background SLA monitor, starter data | Application and Domain |
| **API** | HTTP endpoints, permission checks, turning errors into consistent answers, startup | Everything (composition root) |

Dependencies point inwards, so business rules never depend on the database or HTTP. This keeps them testable.

## 11. What happens inside one request

1. **Authentication**: the bearer token's signature, issuer, audience and expiry are checked. No valid token → `401`.
2. **Authorization**: the endpoint's `[HasPermission(...)]` label is compared with the user's effective
   permissions (cached per user, cleared on any role or permission change). Missing → `403`.
3. **Endpoint**: a thin controller hands the request to one service method.
4. **Validation**: the service checks the input (FluentValidation). Problems → `400` with a message per field,
   which the web application shows next to that field.
5. **Business rules**: e.g. "a closed ticket can't be commented on". Breaking one → `422`, a duplicate → `409`,
   a missing record → `404`.
6. **Save**: audit fields (created/updated by and at) are stamped, the audit entries are captured, and the
   change plus its audit rows are committed in one transaction.
7. **Answer**: JSON. All errors use the same RFC 7807 "problem details" shape, so the web application handles them
   in one place.

## 12. Key decisions and why

| Decision | Why |
|---|---|
| Permissions defined in code, roles and grants as data | A permission only means something if code checks it; administrators still control who has which. New modules' permissions appear in the UI automatically. |
| Permission check on every request (cached), not stored in the token | Revoking access works immediately instead of when the token expires. |
| One role per user | Simple to reason about: a user can do exactly what their role says. |
| Short access token + single-use renewal token (only its hash stored) | A stolen access token is useless within minutes; a copied renewal token is detected on reuse; a database leak exposes no usable tokens. |
| The ticket workflow is one table in Domain | The service enforces it, the API returns the allowed next steps, the screen shows only those buttons: all three always agree. |
| History and audit entries written by the server in the same save | They can't be forgotten, faked or lost halfway. |
| SLA deadlines stored on the ticket | "Breached" and "at risk" become simple date comparisons, fast in lists and filters. |
| Rule engine takes "now" as input | The background job passes the real time; tests pass a future time, so no test waits for the clock. |
| Agents are users whose role can *work tickets* | No extra "is agent" flag to keep in sync with permissions. |
| Files stored on disk under random names, served only as downloads | Uploaded content can never run in the browser, and original file names are never used as paths. |

## 13. Security in short

* Passwords hashed by ASP.NET Core Identity. Password policy: 8+ characters with upper and lower case, a digit and a symbol.
* Lockout after 5 failures, and a per-IP rate limit on sign-in and token renewal.
* The signing key isn't stored in source control for production; the API refuses to start without a valid one.
* CORS limited to the web application's address; HTTPS and HSTS outside development.
* The web application sends the token only to the API and follows only in-app return links after sign-in.
* Attachments: size and type allow-list, random storage names, `Content-Disposition: attachment`, `nosniff` header.
* Server-side checks on every endpoint. Hidden buttons are a convenience, not the protection.

## 14. Data

* SQL Server through Entity Framework Core migrations (`src/Portal.Infrastructure/Persistence/Migrations`).
  Migrations and starter data run at startup and are safe to repeat.
* All times are stored in UTC and sent with a `Z`; the browser shows local time.
* Deletion rules protect history. A customer or category that is in use can't be deleted. Deleting a user
  unassigns their tickets instead of deleting them. A ticket's history goes with the ticket. Tasks linked to a
  deleted ticket are kept but unlinked.

```bash
# after changing the data model
dotnet ef migrations add <Name> -p src/Portal.Infrastructure -s src/Portal.API -o Persistence/Migrations
```

## 15. Web application

Angular with standalone components and signals. One design-token file defines every color, font size and
spacing value, and a small shared set of components (dialogs, toasts, confirmations, toggles, pagination, form
fields, badges) makes every page look and behave the same. The HTTP layer adds the token, renews sessions,
and turns every error into one consistent shape. Details: [`../Portal.FrontEnd/README.md`](../../Portal.FrontEnd/README.md).

## 16. Adding a new module

1. Write the specification (`docs/specs`) and the plan (`docs/plan`).
2. Add its permissions to the permission catalogue (Domain).
3. Add entities (Domain), then the database mapping and a migration (Infrastructure).
4. Add the service with validation and business rules (Application).
5. Add a thin controller with `[HasPermission]` (API).
6. Add tests for every acceptance criterion.
7. Build the pages from the shared components, protect the route with the permission, and add the menu entry.
