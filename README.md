# Portal — Customer Support CRM

Portal is a web application for customer support teams. It keeps every customer, every request and every
conversation in one place, makes sure each request has an owner and a deadline, and records who did what.

This repository is the **backend API**. The web application lives in [`../Portal.FrontEnd`](../Portal.FrontEnd).

| Part | Folder | Technology |
|---|---|---|
| API | `Portal.API` (this folder) | .NET 10, ASP.NET Core Web API, Entity Framework Core 10, ASP.NET Core Identity, JWT, SQL Server |
| Web application | [`../Portal.FrontEnd`](../Portal.FrontEnd) | Angular 20 (standalone components, signals), shared SCSS design system |

A plain-English overview of every module for non-technical readers is in
[`docs/Portal-Service-Catalog.docx`](docs/Portal-Service-Catalog.docx).

---

## What the application does

### Users, roles and permissions
* Staff sign in with email and password. Sessions renew silently in the background and end on sign-out.
* Every user has **one role**, and every role is a set of **permissions** that administrators switch on or off with toggles.
* Every page, button and API call is protected by a permission, so a change to a role applies on the user's next action.
* The system protects itself: the Administrator role always has every permission, and no one can lock out the last administrator or themselves.
* Accounts lock for 5 minutes after 5 wrong passwords, and administrators can deactivate users or reset passwords.

### Customers
* Customer profiles for companies and individuals, each with a readable code (`CUS-00042`).
* Contact details, a preferred channel and language, an address, and contact persons with one primary contact.
* **Interaction history**: calls, emails, meetings and chats, logged in time order. They can't be edited afterwards, because they are history.
* Internal **notes** and **file attachments** (PDF, images, Office and text files up to 10 MB).
* Search by name, email, phone or code. Every customer must be reachable, and duplicate emails are refused.

### Tickets
* Every customer request becomes a ticket (`TCK-00042`) with a category, priority, channel and owner.
* A clear **workflow**: New → Open → In progress → On hold → Resolved → Closed, with reopening. The screen only offers the steps that are allowed next.
* **Assignment** to agents. Agents can take unassigned tickets themselves; supervisors can assign anyone.
* **Escalation** for urgent situations. It requires a reason, raises the priority and makes the ticket stand out everywhere.
* A complete **timeline** per ticket: every change and comment, with who and when.
* Categories are managed by administrators; customers who have tickets can't be deleted.

### Agent dashboard
* The landing page for agents: their tickets in order of urgency, with the customer's details next to each one.
* The **unassigned queue**, with one-click *Take*.
* Personal **tasks and reminders** (e.g. "call back Thursday"), shown as a badge in the top bar when due.
* **Quick replies**: saved answers that fill in the customer's name, the agent's name and the ticket code.
* **Team view**: each agent's workload and what teammates did on your tickets.

### SLA & automation
* **Response and resolution targets** per priority (e.g. Urgent: reply within 30 minutes, resolve within 4 hours).
* Every ticket shows whether it is on track, at risk, breached or met, and the list can show only breached or at-risk tickets.
* Optional **automatic assignment** of new tickets to the least busy agent.
* **Escalation rules** checked every minute, e.g. "when the resolution target is missed, escalate and notify supervisors".
* In-app **notifications** (bell icon) for assignments, escalations and rule alerts.

### Audit log
* Every change to users, roles, permissions, customers, tickets and settings is recorded automatically with who, when, from which IP address, and the before and after values.
* Security events are recorded too: sign-ins, failed attempts, lockouts, password changes and suspicious session reuse.
* Passwords and secrets never appear in the log, and the log can't be edited or deleted through the application.

---

## Quick start

**You need:** .NET SDK 10, SQL Server (the default local instance `.`, or change the connection string), and Node.js 22+.

```bash
# 1. API: creates the PortalDb database, applies migrations and adds starter data on first run
cd Portal.API
dotnet run --project src/Portal.API --launch-profile http
#    → API documentation at http://localhost:5120/swagger

# 2. Web application (second terminal)
cd ../Portal.FrontEnd
npm install
npm start
#    → http://localhost:4200
```

Sign in with **admin@portal.local / Admin@12345**. This account is created in development only.

### Demo data
To fill every module with realistic data (9 users in 6 roles, 14 customers, 23 tickets in every status and SLA state,
rules, tasks, notifications and an audit history), stop the API and start it once with:

```bash
dotnet run --project src/Portal.API --launch-profile http -- --reset-demo
```

⚠️ This **deletes all existing data** first, and only works in Development. The accounts, the data and a 15-minute
demo script are in [docs/demo.md](docs/demo.md).

### Starter data created on first run
| What | Details |
|---|---|
| Roles | *Administrator* (every permission, can't be changed) and *Agent* (no permissions until you grant them) |
| Administrator account | From `Seed:AdminEmail` / `Seed:AdminPassword` |
| Ticket categories | General, Billing, Technical, Account, Complaint |
| Quick replies | Acknowledge request, Ask for more details, Confirm resolution |
| SLA targets | Urgent 30 min / 4 h · High 2 h / 1 day · Medium 8 h / 3 days · Low 1 day / 5 days |

### Configuration
| Setting | Where | Meaning |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `appsettings.json` | SQL Server database. Default `Server=.;Database=PortalDb;Trusted_Connection=True`. Keep MARS off: it disables savepoints that the audited save relies on. |
| `Jwt:Key` | `appsettings.Development.json` | Signing key (at least 32 characters). It is empty in `appsettings.json` on purpose; outside development set it with the `Jwt__Key` environment variable or user-secrets. The API won't start without it. |
| `Jwt:ExpiryMinutes` / `Jwt:RefreshTokenDays` | `appsettings.json` | Session lengths: access token 15 minutes, renewal token 7 days |
| `Seed:AdminEmail` / `Seed:AdminPassword` | `appsettings*.json` | The first administrator. It is only created when a password is set. |
| `Cors:AllowedOrigins` | `appsettings.json` | Where the web application runs (`http://localhost:4200`) |
| `Sla:MonitorEnabled` / `Sla:EvaluationIntervalSeconds` | `appsettings.json` | Background check of escalation rules, every 60 seconds by default |
| `Storage:RootPath` | optional | Folder for uploaded customer files. Default `App_Data/uploads` next to the API; not in git. |
| `Database:InitializeOnStartup` | `appsettings.json` | Apply migrations and starter data at startup. Safe to repeat; turn it off if a deployment pipeline applies migrations. |
| `RateLimiting:LoginPermitsPerMinute` / `RefreshPermitsPerMinute` | optional | Protection against password guessing: 10 sign-ins and 60 renewals per minute per IP |

---

## Tests

```bash
dotnet test                                 # backend: 149 unit and integration tests
cd ../Portal.FrontEnd && npm run test:ci    # frontend: 65 unit tests in headless Chrome
```

The backend tests start the real API in memory against a temporary SQLite database, so they need no SQL Server
and leave nothing behind. [docs/verification.md](docs/verification.md) explains, in business terms, what each
group of tests proves.

---

## Documentation

| Document | For whom | What's in it |
|---|---|---|
| [Portal-Service-Catalog.docx](docs/Portal-Service-Catalog.docx) | Everyone | Purpose of the application and what each module does, in plain English |
| [demo.md](docs/demo.md) | Presenters, testers | Resetting to the demo data, the demo accounts, and a 15-minute walkthrough of every module |
| [architecture.md](docs/architecture.md) | Product owners and developers | How things happen step by step, then the technical design |
| [verification.md](docs/verification.md) | Testers, reviewers | Every business rule, the scenario that proves it, and the test that checks it |
| [specs/](docs/specs) | Product owners and developers | One specification per module: goal, assumptions, rules, API and acceptance criteria |
| [plan/](docs/plan) | Developers | Technical approach and task list per module |

| # | Module | Specification | Plan |
|---|---|---|---|
| 1 | Users, roles and permissions | [001](docs/specs/001-identity-access.md) | [001](docs/plan/001-identity-access-tasks.md) |
| 2 | Session renewal | [002](docs/specs/002-refresh-tokens.md) | — |
| 3 | Customers | [003](docs/specs/003-customer-management.md) | [003](docs/plan/003-customer-management-tasks.md) |
| 4 | Tickets | [004](docs/specs/004-ticket-management.md) | [004](docs/plan/004-ticket-management-tasks.md) |
| 5 | Agent dashboard | [005](docs/specs/005-agent-dashboard.md) | [005](docs/plan/005-agent-dashboard-tasks.md) |
| 6 | Audit log | [006](docs/specs/006-audit-logs.md) | [006](docs/plan/006-audit-logs-tasks.md) |
| 7 | SLA & automation | [007](docs/specs/007-sla-automation.md) | [007](docs/plan/007-sla-automation-tasks.md) |

## Repository layout

```
docs/                     Service catalog, architecture, verification, specs and plans
src/
  Portal.Domain/          Business entities and rules that need nothing else (workflow, SLA arithmetic, permission list)
  Portal.Application/     Use cases: services, validation, business rules
  Portal.Infrastructure/  Database, sign-in, tokens, file storage, audit trail, background SLA monitor, starter data
  Portal.API/             HTTP endpoints, permission checks, error responses, application startup
tests/
  Portal.Tests/           Unit tests and end-to-end API tests
```

## How the project is built

Each module goes through the same steps: **specification → plan → implementation → tests → verification →
review in the running application**. The specification and plan are committed before the code, and each layer is
committed separately, so the history reads like the plan.
