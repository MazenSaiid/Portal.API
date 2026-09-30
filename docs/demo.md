# Portal demo guide

Everything you need to reset the system to a known state and give a 15-minute demo of every module.

---

## 1. Reset to the demo data

> ⚠️ **This deletes everything** in the `PortalDb` database and every uploaded file, then loads the demo data below.
> It only runs in the Development environment. The API refuses the switch anywhere else.

```bash
# stop the API if it is running, then:
cd Portal.API
dotnet run --project src/Portal.API --launch-profile http -- --reset-demo
```

After about 10 seconds the log shows `Demo data seeded: 9 users, 14 customers, 23 tickets, …` and the API keeps running
at http://localhost:5120, ready for the demo. Start the web application as usual (`cd ../Portal.FrontEnd && npm start`)
and open http://localhost:4200.

**Reset shortly before you present.** All dates are relative to the moment of the reset ("created 3 days ago",
"due in 48 minutes"), so the deadlines are real and they run down in real time. To start again, stop the API and run the
same command. Starting the API normally (without `--reset-demo`) keeps the data.

---

## 2. Who's who

Every demo account uses the password **`Demo@12345`**. The administrator keeps its usual password.

| Person | Sign in with | Role | Use this account to show |
|---|---|---|---|
| System Administrator | `admin@portal.local` / `Admin@12345` | Administrator | Everything. The main demo account: owns 2 tickets and has tasks and notifications |
| Sara Khan | `sara.khan@portal.local` | Supervisor | Assigning work, SLA rules, supervisor alerts, the audit log |
| Omar Haddad | `omar.haddad@portal.local` | Agent | An agent's day. He has the fewest active tickets, so auto-assignment picks him first |
| Lina Farouk | `lina.farouk@portal.local` | Agent | Owner of the urgent, escalated pharmacy outage |
| Youssef Nasser | `youssef.nasser@portal.local` | Agent | Owner of two at-risk tickets |
| Nadia Saleh | `nadia.saleh@portal.local` | Customer Success | Customer records; she can log tickets but not work them |
| Karim Aziz | `karim.aziz@portal.local` | Auditor | Read-only access, including the audit log. Lands on *Overview*, not the dashboard |
| Rami Toufic | `rami.toufic@portal.local` | Trainee | Can only look at tickets and customers. Good for a live permission change |
| Hana Ibrahim | `hana.ibrahim@portal.local` | Agent (**deactivated**) | Sign-in is refused: "Your account is disabled" |

### What each role can do

| Module | Administrator | Supervisor | Agent | Customer Success | Auditor | Trainee |
|---|---|---|---|---|---|---|
| Dashboard | ✔ | ✔ | ✔ | – | – | – |
| Tickets | everything | everything | view, create, edit, work, escalate | view, create | view | view |
| Customers | everything | everything | view, create, edit, log activity | everything | view | view |
| Quick replies (manage shared) | ✔ | ✔ | – (can use them) | – | – | – |
| SLA & automation | ✔ | ✔ | – | – | – | – |
| Audit log | ✔ | ✔ | – | – | ✔ | – |
| Users | everything | view | – | – | view | – |
| Roles & permissions | everything | view | – | – | view | – |

Try deleting the *Trainee* role: it's refused because Rami still has it. A role in use can't be deleted.

---

## 3. What's in the system

### Customers (14)

| Code | Customer | Type | Worth showing |
|---|---|---|---|
| CUS-00001 | Al Noor Trading LLC (Dubai) | Company | **The best profile to show:** 2 contacts, meeting/email/call history, 2 notes, an attached service agreement, 2 tickets |
| CUS-00002 | Nile Valley Pharmacies (Cairo) | Company | Arabic-speaking; the urgent POS outage |
| CUS-00003 | Gulf Star Logistics (Riyadh) | Company | Attached delivery-zones file; a ticket escalated by a rule |
| CUS-00004 | Blue Harbor Hotels (Muscat) | Company | A ticket that was resolved, reopened and resolved again |
| CUS-00005 | Cedar Tech Solutions (Beirut) | Company | Prefers WhatsApp; 2 technical tickets |
| CUS-00006 | Delta Foods Co. (Alexandria) | Company | A complaint escalated by the supervisor |
| CUS-00007 | Horizon Academy (Amman) | Company | 2 tickets waiting in the queue |
| CUS-00008 | Sunrise Clinics (Doha) | Company | The urgent ticket the SLA monitor escalates live |
| CUS-00009 – 00013 | Ahmed Mostafa, Sarah Johnson, Fatima Al-Zahra, Daniel Kim, Youssef Barakat | Individuals | Mixed channels (WhatsApp, email, SMS, phone) and languages |
| CUS-00014 | Old Town Bakery | Company, **inactive** | Closed account kept for history; it has no tickets, so it can be deleted |

### Tickets (23)

| Code | Subject | Status | Owner | SLA right after the reset | What it shows |
|---|---|---|---|---|---|
| TCK-00001 | Data export for the annual audit | Closed | Sara | Met | A normal, complete lifecycle |
| TCK-00002 | Change billing contact | Closed | Omar | Met | Quick fix |
| TCK-00003 | Wrong item delivered | Closed | Sara | **Resolved late** | A missed target stays on record after closing |
| TCK-00004 | Update phone number | Closed | Omar | Met | SMS channel |
| TCK-00005 | Wi-Fi voucher printer offline | Resolved | Youssef | Met | **Reopened** once, then resolved again |
| TCK-00006 | Charged in the wrong currency | Resolved | Omar | Met | |
| TCK-00007 | Refund for cancelled subscription | Resolved | Lina | Met | |
| TCK-00008 | Invoice INV-2291 charged twice | In progress | Omar | **Breached** | **Escalated automatically** by the rule "Resolution missed", with the full story in its timeline |
| TCK-00009 | Monthly report export times out | In progress | Lina | **Breached** | Rule escalation also **raised Medium → High** |
| TCK-00010 | Dark mode feature request | On hold | Youssef | **At risk** | The owner was warned by the "at risk" rule |
| TCK-00011 | Three new receptionist accounts | On hold | Omar | On track | Waiting for the customer |
| TCK-00012 | Complaint: delivery driver was rude | In progress | Sara | **Breached** | **Escalated by hand** with a reason |
| TCK-00013 | Add the Zayed branch to the contract | Open | Lina | On track | Assigned by the supervisor |
| TCK-00014 | API returns 500 on bulk upload | In progress | Youssef | **At risk** (due in ~4 h) | |
| TCK-00015 | Password reset email not arriving | In progress | Youssef | On track | |
| TCK-00016 | Tracking page shows the wrong ETA | In progress | Sara | On track | |
| TCK-00017 | Pharmacy POS cannot sync stock | In progress | Lina | **At risk** (due in ~50 min) | Urgent, escalated by hand, deadline runs out during the demo |
| TCK-00018 | Question about payment terms | Open | Admin | On track | On the admin's dashboard |
| TCK-00019 | SSO login loop for some users | In progress | Admin | On track | On the admin's dashboard |
| TCK-00020 | Copies of last year's invoices | **New** | — | On track | In the queue; supervisors were alerted after an hour |
| TCK-00021 | Appointment reminders not sending SMS | **New** | — | **Response overdue** | Urgent and unanswered; the SLA monitor escalates it |
| TCK-00022 | Teachers can't upload exam files | **New** | — | On track | In the queue. **Take this one live** |
| TCK-00023 | Can't log in to the customer portal | **New** | — | On track | In the queue |

### SLA & automation

* **Targets** (first response / resolution): Urgent 30 min / 4 h · High 2 h / 1 day · Medium 8 h / 3 days · Low 1 day / 5 days.
* **Automatic assignment:** **off**. Switch it on during the demo.
* **Escalation rules:**

| Rule | When | Then | Active |
|---|---|---|---|
| Urgent work not answered in time | First response missed, High or Urgent | Escalate, notify supervisors | ✔ |
| Warn owner when resolution is at risk | 75 % of the resolution time used | Notify the owner | ✔ |
| Resolution missed | Resolution deadline passed | Escalate, notify owner and supervisors | ✔ |
| Unassigned for an hour | Nobody owns it after 60 min | Notify supervisors | ✔ |
| Weekend backlog sweep | Unassigned for 24 h | Notify supervisors | ✖ (switched off) |

### What happens by itself after the reset

The SLA monitor checks the rules every minute. The rules have already run on every ticket that matched before the reset, so
nobody gets a flood of alerts. What's still ahead happens live:

| About … after the reset | What happens |
|---|---|
| 1 minute | **TCK-00021 is escalated automatically** ("Urgent work not answered in time"), and Sara and the admin get a bell notification |
| 10 minutes | TCK-00021 has been unassigned for an hour, so the supervisors are alerted again |
| 30 / 35 minutes | Same alert for TCK-00022 and TCK-00023, unless someone has taken them |
| 50 minutes | TCK-00017's resolution deadline passes, so Lina and the supervisors are notified |
| 90 minutes | TCK-00022 escalates too, if it's still unanswered |

### Also seeded

* **Tasks:** the admin has an **overdue** task, one due today (linked to TCK-00008 and Al Noor), one next week and one
  done. Sara, Omar, Lina and Youssef have their own. The calendar badge in the top bar counts what's due.
* **Quick replies:** 5 shared (Acknowledge request, Ask for more details, Confirm resolution, Escalation acknowledgement,
  Closing after no response), a personal Arabic greeting for the admin and a personal one for Omar.
* **Notifications:** assignment, escalation and SLA alerts. Anything older than 12 hours is already read.
* **Categories:** General, Billing, Technical, Account, Complaint, Delivery, plus *Legacy hardware*, which is switched off.
* **Audit log (~275 entries):** how the team was set up (roles, permission grants, users) and who created and changed
  customers and tickets. Security events too:
  * daily sign-ins;
  * two wrong passwords by Youssef;
  * **Karim locked out** after repeated failures;
  * three guesses at non-existent admin accounts from the outside address `203.0.113.45`;
  * Hana's refused sign-in as a deactivated user;
  * Lina's password change.

---

## 4. The 15-minute demo script

### ① The agent's day: dashboard (2 min)
Sign in as **admin**. You land on the dashboard.
* The tiles show 2 active tickets and 4 in the unassigned queue. Click a tile to open the filtered list.
* **My tasks:** one overdue (red) and one due today, linked to TCK-00008.
* **Team activity:** what teammates did recently.
* **The bell** 🔔 shows unread alerts, including the one the SLA monitor raised for TCK-00021 a minute after the reset.

### ② Tickets and SLA (4 min)
Open **Tickets**.
* The red flag marks escalated tickets. The SLA column shows *Response overdue*, *Due in 48m* (amber) and *Overdue*.
* Set the SLA filter to *Breached*, then click **Escalated**: the supervisor's to-do list.
* Open **TCK-00008**. Walk down the timeline:
  1. Nadia logged the ticket and assigned it to Omar.
  2. Omar replied (first response *met*) and put it on hold waiting for the bank.
  3. The deadline passed, so **System** escalated it with the rule's name.
  4. Omar picked it up again.

  The SLA card on the right says the same thing.
* Back on the dashboard, click **Take** on **TCK-00022**. It moves to *My tickets*.
* Open it and click **⚡ Quick replies → Acknowledge request**. The customer name, your name and the ticket code are
  filled in. Add the comment: the first response turns **Met**.
* Change the status to *In progress*, then escalate it with a reason. The priority is raised to High if it was lower.

### ③ Customers (2 min)
Open **Customers → Al Noor Trading (CUS-00001)**.
* The profile, the primary contact (Khalid) and the preferred channel.
* **Interactions:** a meeting, an email and a call, in time order. They can't be edited, because they are history.
* **Notes** and the attached **service agreement**: download it.
* The **Tickets** tab shows the open double-charge ticket and the closed billing-contact change.
* Try to delete Al Noor: it's refused because it has tickets. Old Town Bakery (inactive, no tickets) can be deleted.

### ④ Automation (2 min)
Open **SLA & automation**.
* The targets per priority, and the five rules (one switched off).
* **Switch on automatic assignment.** Create a new ticket for any customer without choosing an owner. It goes to
  **Omar**, who has the fewest active tickets. The timeline says "Assigned automatically …".
* Sign in as Omar in a private window: the bell shows "assigned to you".

### ⑤ Roles and permissions: nothing is hard-coded (3 min)
Open **Roles & permissions**.
* The *Administrator* role is locked: it always has everything.
* Open **Trainee → permissions** and switch on **Create tickets**.
* In a private window, sign in as **Rami**. The *New ticket* button is there now. Switch it off again, and after Rami
  signs in again the button is gone.
* **Users:** Hana is deactivated. Try signing in as her: refused.
* Sign in as **Karim** (auditor): he lands on *Overview* and can read tickets, customers, users, roles and the audit
  log, but there are no edit buttons anywhere.

### ⑥ Audit log: who did what (2 min)
Open **Audit log**.
* The permission you just toggled on Trainee is at the top, with your name, IP and time.
* Filter the actions by *Locked out* (Karim) or *Sign-in failed*. Look for `203.0.113.45`: someone guessing admin accounts.
* Open a ticket or customer change to see its *before → after*.
* The log can't be edited or deleted from the application.

**Optional, live lockout:** in a private window, enter a wrong password 5 times for `rami.toufic@portal.local`. The
account is locked for 5 minutes, and the lockout appears in the audit log.

---

## 5. Troubleshooting

| Problem | Fix |
|---|---|
| `Could not copy … Portal.API.dll … used by another process` | The API is still running. Stop it first, then run the reset |
| `--reset-demo … only runs in the Development environment` | Use the `http` launch profile (it sets `ASPNETCORE_ENVIRONMENT=Development`) |
| `Demo data needs the administrator account` | `Seed:AdminPassword` is missing from `appsettings.Development.json` |
| The admin sees fewer alerts than described | Some alerts only appear after the times listed in *What happens by itself after the reset* |
| The demo happened long after the reset | Deadlines kept running, so more tickets are overdue. Run the reset again |
