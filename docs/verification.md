# How Portal is verified

This document lists every business promise Portal makes, **why it matters**, **the scenario that proves it**, and
**where the check lives**. Each scenario is written as *Given* (the situation), *When* (what someone does) and
*Then* (what must happen). The test names in the last column let a developer find and run the exact check.

**Status:** all **149 backend tests** and **65 frontend tests** pass. Every module was also walked through in a real
browser against SQL Server; the problems that walkthrough found, and how they were fixed, are listed at the end of
each module section.

```bash
dotnet test                                 # backend (Portal.API)
npm run test:ci                             # frontend (Portal.FrontEnd)
```

### What the three kinds of check prove

| Kind | What it does | Why it's trustworthy |
|---|---|---|
| **End-to-end API tests** | Start the real API in memory with a fresh database, sign in as real users with real roles, and call it over HTTP, exactly like the browser does | The permission checks, validation, business rules, database and error answers all run as in production |
| **Unit tests** | Check one rule in isolation, e.g. SLA arithmetic, the workflow table, form validation, a page's buttons | Fast and precise; they pin down edge cases |
| **Browser walkthrough** | A script clicks through the running application and takes screenshots | Catches what tests can't see: layout, wording, a button that does nothing |

**Do the tests actually catch mistakes?** Twice, a rule was broken on purpose to see if the tests noticed:
* Switching off the permission-cache refresh made *"Granting and revoking a permission takes effect on the next request"* fail.
* Removing the "fire only once" guard from the escalation engine made *"…escalates and notifies supervisors exactly once"* fail.

Both were restored afterwards.

---

## 1. Users, roles and permissions

### Signing in
| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| A correct sign-in returns the user's role and permissions | The screen can show the right menu at once | **Given** the administrator account **When** they sign in **Then** they receive a session and all 26 permissions | `AuthTests.Login_with_valid_credentials_returns_token_role_and_permissions` |
| A wrong password never reveals whether the email exists | Stops attackers from collecting valid emails | **Given** a real email and a made-up one **When** both are tried with a wrong password **Then** both get the same "Invalid email or password" | `Login_with_bad_credentials_returns_401_with_generic_message` |
| Deactivated users can't sign in | Leavers lose access immediately | **Given** a user who was deactivated **When** they sign in with the correct password **Then** they're refused with "account disabled" | `Inactive_user_cannot_sign_in` |
| Five wrong passwords lock the account | Slows down password guessing | **Given** 5 wrong attempts **When** the right password is used **Then** it's still refused, with "locked" | `Account_locks_after_five_failed_attempts` |
| Nothing works without signing in | No anonymous access to data | **When** users, roles, permissions or profile are requested without a session **Then** all answer 401 | `Protected_endpoints_without_token_return_401` |
| Changing your password requires the current one | Someone at an unlocked computer can't take over the account | **When** the current password is wrong **Then** it's refused on that field; with the right one it succeeds and the new password works | `Change_password_rejects_wrong_current_password_and_accepts_correct_one` |

### Permissions apply immediately
| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| Missing permission → refused | The server is the real gatekeeper | **Given** a user whose role has no permissions **When** they open users or roles **Then** 403; their own profile still works | `PermissionEnforcementTests.User_without_permission_gets_403` |
| Granting or revoking works on the next click | No waiting and no forced sign-out | **Given** a signed-in user **When** an admin grants *View users* **Then** the next request succeeds; **when** it's revoked **then** the next one is refused | `Granting_and_revoking_a_permission_takes_effect_on_the_next_request` |
| Deactivation ends access even with a valid session | A session that's still open is not a loophole | **Given** a signed-in user **When** an admin deactivates them **Then** their very next request is refused | `Deactivated_user_loses_access_immediately_with_an_existing_token` |
| Moving a user to another role changes their access at once | Role changes are how access is managed | **When** a user's role changes to one without *View users* **Then** their next request is refused | `Moving_a_user_to_another_role_changes_their_permissions_immediately` |
| Screens and menus follow permissions | People only see what they can use | Guards send users without permission to *Access denied*; buttons without permission aren't rendered | `guards.spec.ts`, `has-permission.spec.ts`; walkthrough |

### Managing users and roles
| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| Invalid input is explained field by field | People can fix mistakes quickly | **When** a user is saved with no name, a bad email, a bad phone and a short password **Then** each field gets its own message | `UsersTests.Create_with_invalid_payload_returns_400_with_field_errors`, `…weak_password…` |
| One account per email, regardless of capitals | No duplicate people | **Given** `dup@x.com` exists **When** `DUP@X.COM` is added **Then** it's refused as a conflict | `Create_with_duplicate_email_returns_409_case_insensitively` |
| Search, filter by role, sort and page | Finding people in a large team | **Given** 3 users with a tag in one role **When** page 2 of size 2 is asked, sorted by email **Then** exactly the third one comes back | `List_supports_search_role_filter_and_paging` |
| You can't lock yourself out | Prevents accidental self-lockout | **When** an admin tries to delete, deactivate or change the role of their own account **Then** each is refused | `Admin_cannot_lock_themselves_out` (3 cases) |
| The last administrator is protected | The system always has someone in charge | **Given** a user manager (not an admin) **When** they try to deactivate or delete the only admin **Then** both are refused | `Last_active_administrator_cannot_be_deactivated_by_another_user` |
| The Administrator role can't be weakened | Always a way back in | **When** someone tries to delete, rename or remove a permission from *Administrator* **Then** all refused; editing its description is allowed | `RolesTests.System_role_cannot_be_deleted_renamed_or_have_permissions_changed` |
| A role in use can't be deleted | Users would be left without access | **Given** a role with one user **When** it's deleted **Then** refused; an empty role deletes fine | `Role_with_users_cannot_be_deleted_but_empty_role_can` |
| Toggling a whole module at once | Faster role setup | **When** all *Users* permissions are granted in one step **Then** all four are on and *Roles* untouched; granting again changes nothing | `Granting_a_whole_module_then_revoking_it` |
| A toggle that fails snaps back | The screen never lies about what's saved | **Given** the server refuses a toggle **Then** the switch returns to its previous position | `role-permissions.spec.ts` |

**Walkthrough:** sign-in, users, roles and the permissions screen were checked at desktop and phone width.
The page-size selector was too narrow and cut off "10"; it was widened.

## 2. Staying signed in (session renewal)

| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| Sessions renew silently | People aren't interrupted every 15 minutes | **When** the renewal token is used **Then** a new pair comes back and the new access token works | `RefreshTokenTests.Refresh_rotates_the_token_pair_and_the_new_access_token_works` |
| A copied renewal token is detected | Stolen sessions are shut down | **Given** a renewal token that has already been used **When** it's used again **Then** it's refused **and** the newer one stops working too | `Reusing_a_rotated_token_is_rejected_and_revokes_the_newer_one_too` |
| Signing out really ends the session | Shared computers are safe | **When** a user signs out **Then** the renewal token no longer works; signing out twice is harmless | `Logout_revokes_the_refresh_token_and_is_idempotent` |
| Deactivation and password reset end sessions | An admin action takes effect everywhere | **When** a user is deactivated, or an admin resets their password **Then** renewal is refused | `Deactivated_user_cannot_refresh`, `Admin_password_reset_ends_the_users_sessions` |
| Changing your password signs out other devices only | Your current session continues | **When** a user changes their password **Then** their old renewal token stops working and the new one works | `Change_password_returns_a_new_session_and_revokes_the_old_one` |
| The browser renews once, even with many requests at the same time, and keeps tabs in sync | No accidental "reuse" alarms from your own tabs | Several requests fail together → one renewal; another tab's newer token is adopted instead of reusing an old one | `auth.service.spec.ts`, `auth.interceptor.spec.ts` |

**Found during development:** after a password change, another device trying to renew was treated as theft, which
also ended the new session. Reuse detection now only applies to tokens that were renewed, not to tokens ended by
sign-out or a password change.

## 3. Customers

| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| Create, view, edit and delete customers, with who and when | The basic customer record | **When** a customer is added, renamed and deleted **Then** each step works, and "created by / updated by" show the admin | `CustomersTests.Create_read_update_delete_customer` |
| Every customer must be reachable | A record nobody can contact is useless | **When** a customer has neither email nor phone **Then** refused on the email field | `Customer_without_email_and_phone_is_rejected` |
| The preferred channel must be usable | Don't promise WhatsApp without a phone number | **When** WhatsApp is preferred but there's no phone **Then** refused with "requires a phone number" | `Preferred_channel_must_be_reachable`; `CustomerRuleTests`; `customer-form-dialog.spec.ts` |
| One customer per email | No duplicate customers | **Given** an existing email **When** it's added again in capitals **Then** conflict; keeping your own email on edit is fine | `Duplicate_email_is_rejected_case_insensitively_but_keeping_your_own_is_fine` |
| Find customers by name or code, with filters | Fast lookup during a call | **When** searching a tag, filtering *Company*, sorting by name **Then** the right ones come back; searching `CUS-00042` finds that customer | `List_supports_search_by_name_and_code_filters_and_paging` |
| The list shows the last interaction | See at a glance who was contacted recently | **When** a call is logged 2 hours ago **Then** the list shows that time | `List_shows_last_interaction_date` |
| Only one primary contact | Clear "who do we call" | **When** a second contact is marked primary **Then** the first one stops being primary | `Contacts_can_be_managed_with_a_single_primary` |
| Interactions are history and can't be rewritten | Trustworthy history | **Then** there's no way to edit or delete an interaction, and one dated in the future is refused | `Interactions_are_logged_and_listed_newest_first`, `…future_is_rejected`, `…cannot_be_edited_or_deleted` |
| Notes belong to their author | Colleagues can't rewrite each other's notes | **Given** agent A's note **When** agent B tries to edit or delete it **Then** refused; A can edit; an editor can delete | `Only_the_author_or_an_editor_can_change_a_note`; `customer-notes.spec.ts` |
| Files are safe | Uploads can't harm users | **When** an `.exe`, `.html`, empty or over-10-MB file is uploaded **Then** refused; a PDF with a path in its name is stored under a clean name and always downloaded, never opened in the browser | `Upload_download_and_delete_an_attachment`, `Disallowed_or_empty_files_are_rejected`, `Files_over_10_MB_are_rejected` |
| Deleting a customer removes their files | Nothing left behind on disk | **When** a customer with a file is deleted **Then** the file is gone from storage | `Deleting_a_customer_removes_its_files_from_storage` |
| Permissions apply per action | View-only users stay view-only | **Given** a *view customers* user **Then** viewing works, and creating, editing, deleting or adding notes is refused | `Customer_endpoints_require_permissions` |

**Walkthrough found and fixed:** the *Add note* button did nothing (now covered by a test), and the customer page
overflowed on phones (fixed and checked at 390 px width).

## 4. Tickets

| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| A new ticket keeps everything that was entered | Nothing gets lost | **When** a High-priority ticket is created **Then** it has a `TCK-` code, status *New*, the category, the customer, the creator, and a *Created* line on its timeline | `TicketsTests.Creating_a_ticket_persists_every_field_and_records_history` |
| Assigning at creation starts the ticket as Open | Assigned work is visibly in someone's hands | **When** created with an assignee **Then** status *Open*, and the timeline shows *Created, Assigned* | `Creating_with_an_assignee_starts_open` |
| No tickets for inactive customers | Deactivated customers stay closed | **Given** an inactive customer **When** a ticket is opened **Then** refused | `Tickets_cannot_be_opened_for_an_inactive_customer` |
| Filters find the right work | Agents and supervisors see their queue | Status (several at once), priority, *assigned to me*, *unassigned*, escalated, code search, and sorting by severity (Urgent first, not alphabetical) all return exactly the right tickets | `List_filters_by_status_priority_assignee_customer_and_escalation` |
| Every edit is recorded | Accountability | **When** category, priority, subject and channel change **Then** the timeline shows each, with before and after | `Editing_records_each_change_in_history` |
| The workflow is enforced | Tickets can't skip steps | A full life *New → Open → In progress → On hold → Resolved → Closed → Reopened* works, and each step is on the timeline in order; *New → On hold* and "same status again" are refused | `A_ticket_goes_through_its_full_life_and_every_step_is_recorded`, `Disallowed_transitions_are_rejected`; `TicketWorkflowRuleTests` |
| Work needs an owner | No "in progress" ticket without someone working it | **When** an unassigned ticket moves to *In progress* **Then** refused: "Assign … first" | `In_progress_requires_an_assignee` |
| Resolving needs an explanation | The next agent knows what was done | **When** resolved with an empty note **Then** refused on the note | `Resolving_requires_a_resolution_comment`; `ticket-details.spec.ts` |
| Closed means closed | Finished work isn't changed by accident | **Given** a closed ticket **Then** comments, assignment, escalation and edits are refused until it's reopened | `Closed_tickets_are_read_only_until_reopened` |
| Agents take work; supervisors assign it | Clear responsibility | **Given** an agent **Then** they can take an unassigned ticket and release it, but can't assign it to someone else or take a colleague's | `Agent_can_take_an_unassigned_ticket_and_release_it_but_not_assign_others` |
| Only agents can be assigned | Tickets never go to people who can't work them | **When** assigned to a view-only user **Then** refused; the assignee list shows only agents | `Only_active_agents_can_be_assigned_and_are_listed` |
| Escalation raises priority, never lowers it, and needs a reason | Urgent issues get attention, consistently | Low → High on escalation; Urgent stays Urgent; no reason → refused; escalating twice → refused; removing the escalation keeps the priority | `Escalation_raises_priority_and_de_escalation_keeps_it`, `Escalation_never_lowers_priority` |
| Resolving clears the escalation | Finished tickets don't stay red | **When** an escalated ticket is resolved **Then** it's no longer escalated, recorded on the timeline | `Resolving_clears_the_escalation` |
| Categories stay tidy | Reporting and routing depend on them | Names are unique (case-insensitive); a category in use can't be deleted, only deactivated; inactive ones can't be chosen for new tickets | `Categories_are_unique_and_protected_while_in_use` |
| A customer with tickets can't be deleted | Tickets never lose their customer | **Given** a customer with a ticket **When** deleted **Then** refused; after the ticket is deleted, allowed | `Delete_ticket_and_customers_with_tickets_are_protected` |

**Found and fixed:** one test only passed because of the order tests ran in; one ticket-page element used a
feature the permission directive doesn't have (a frontend test caught it); the walkthrough showed resolved tickets
still marked red (rule added) and a workload count that didn't refresh.

## 5. Agent dashboard

| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| The numbers match the agent's real work | Agents trust what they see | **Given** 3 active tickets (1 in progress, 1 escalated, 2 high/urgent) and 1 resolved this week **Then** the tiles show exactly 3 / 1 / 1 / 2 and 1 | `DashboardTests.Summary_and_my_tickets_reflect_the_agents_work_in_priority_order` |
| My tickets are ordered by what needs me most | The next task is always at the top | Escalated first, then Urgent, then Low; each shows the customer and "+2 open" for their other tickets | same test; `agent-dashboard.spec.ts` |
| The queue shows the most urgent waiting work, and taking it moves it | Nothing sits unowned | Urgent appears before Medium; after *Take* it leaves the queue and appears in *my tickets* | `Unassigned_queue_shows_waiting_tickets_most_urgent_first_and_they_can_be_taken` |
| Team activity shows others' work on my tickets | Collaboration without asking around | A colleague's comment appears; my own comment doesn't; workload counts are right | `Team_activity_shows_what_others_did_on_my_tickets_but_not_my_own_actions` |
| Tasks: add, edit, complete, reopen, delete | A simple personal to-do list | Full cycle; completed tasks leave the open list and appear under *completed* | `Tasks_can_be_created_edited_completed_reopened_and_deleted`; `my-tasks.spec.ts` |
| Tasks are private | Personal notes stay personal | **Given** my task **When** someone else tries to see, edit, complete or delete it **Then** it simply isn't found | `Other_users_cannot_see_or_touch_my_tasks` |
| Reminders count what's due | Nothing slips | Overdue, due later today, next week, no date → reminders say 1 overdue, 1 today; the list is ordered by due date | `Reminders_count_overdue_and_due_today_tasks` |
| A ticket reminder survives the ticket | Reminders don't vanish silently | **When** the linked ticket is deleted **Then** the task stays, unlinked | `Deleting_a_linked_ticket_keeps_the_task_but_unlinks_it` |
| Quick replies: team and personal | Consistent answers | Agents see the 3 team replies (read-only) and their own, not others' personal ones; only managers change team replies | `Agents_see_seeded_shared_replies_and_their_own`, `Shared_replies_need_the_manage_permission_and_personal_ones_their_owner` |
| Quick replies fill in the details | No copy-paste mistakes | `{customer}`, `{agent}`, `{ticket}` become the real names and code | `fillPlaceholders` spec; walkthrough |
| Tiles open the matching list | One click to act | *Escalated* opens `/tickets?assignedTo=me&escalated=true`, and the list applies it | `agent-dashboard.spec.ts`, `tickets-list.spec.ts` |

**Found:** the permission-list consistency check flagged the readable name "Quick replies" against its key. The check
now ignores spaces and capitals, so names can stay readable.

## 6. Audit log

| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| Every change is recorded with who, when, where and what changed | Accountability and investigations | **When** a customer is created, renamed from *Audit Co* to *Audit Company* (city Riyadh → Dammam) and deleted **Then** three entries appear, newest first, each with the admin's name and IP; the update lists exactly those two changes | `AuditLogTests.Customer_create_update_delete_are_logged_with_user_ip_and_changes` |
| Values are readable | Non-technical reviewers can use it | Status shows *New → Resolved*; the assignee shows *System Administrator*, the category *General*, the customer *Al Noor Trading (CUS-00003)*, not internal numbers | `Ticket_changes_are_logged_with_readable_values`, `Referenced_records_are_shown_by_name` |
| Access changes are recorded in words | Who gave whom which power | *"Permission Users.View granted to role X"*, *"…revoked…"*, *"User x assigned to role X"* | `Permission_grants_and_role_assignments_are_logged_with_names` |
| Security events are recorded | Spot attacks and misuse | An unknown email, 5 wrong passwords, the lockout, sign-in, sign-out and an admin password reset each appear, with the right person | `Security_events_are_logged` |
| No secrets, no noise | The log must not leak or flood | Password hashes and security stamps never appear; a sign-in that only updates "last sign-in time" adds no data entry | `Secrets_and_noise_fields_never_reach_the_log` |
| Easy to search | Find the relevant entry quickly | Text search, action, user and date filters each narrow the results correctly | `Log_can_be_filtered_by_text_action_user_and_date`; `audit-log.spec.ts` |
| Can't be tampered with | The log is evidence | Changing or deleting an entry, even directly through the data layer, is refused | `Audit_entries_cannot_be_changed_or_deleted` |
| Only authorised people can read it | It contains sensitive activity | Without *View the audit log* → refused; without a session → 401 | `Reading_the_log_requires_permission` |

## 7. SLA & automation

| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| Every ticket gets deadlines from its priority | Clear promises to customers | An Urgent ticket is due for first response in 30 minutes and resolution in 4 hours; lowered to Low, it becomes 5 days, still counted from creation | `SlaTests.New_tickets_get_due_dates_from_their_priority_and_priority_changes_recompute_them` |
| The first reply stops the first-response clock | Measures how fast customers hear back | **When** the first comment is added **Then** first response is *met*; a second comment changes nothing | `The_first_comment_counts_as_the_first_response` |
| The traffic light is calculated consistently | Everyone reads the same status | 25 % of the time used → on track; 75 % → at risk; past due → breached; done in time → met, done late → breached, for good | `SlaCalculatorTests` |
| Targets can be changed, sensibly | Each business has its own promises | Managers can change targets, and new tickets use them; resolution faster than first response, or a missing priority, is refused; agents can read but not change | `Targets_can_be_changed_by_sla_managers_only_and_are_validated`; `sla.spec.ts` |
| Breached and at-risk tickets can be listed | Supervisors act before it's too late | Of three tickets (one late, one at risk, one fine), each filter returns exactly the right one | `Ticket_list_filters_breached_and_at_risk_tickets` |
| Automatic assignment balances the load | Fair distribution, no triage delay | Off by default; switched on, a new ticket goes to the agent with no active tickets rather than to those who have one; the timeline says "assigned automatically" and that agent is notified | `AutoAssignTests.New_unassigned_tickets_go_to_the_least_loaded_agent_when_enabled` |
| Rules act when deadlines are missed, **once** | Timely escalation without alert storms | Nothing happens before the deadline; after it the ticket is escalated (priority raised to High, done by *System*) and supervisors are notified; running again does nothing more | `SlaEngineTests.A_breached_resolution_rule_escalates_and_notifies_supervisors_exactly_once` |
| Rules respect priority limits and never lower priority | Automation stays predictable | "Unassigned 30 min, Medium and above → High": Low is ignored, Medium becomes High (deadlines tighten), Urgent stays Urgent | `Unassigned_rule_raises_priority_but_respects_the_minimum_priority_and_never_lowers` |
| Rules must make sense | No silent do-nothing rules | A rule without any action, or "unassigned for" without minutes, is refused | `Invalid_rules_are_rejected`; `sla.spec.ts` |
| People are told what concerns them, and only them | Nothing important is missed | Assigned + escalated → the agent sees 2 notifications, can mark one or all as read; can't touch someone else's; assigning to yourself doesn't notify you | `Assignment_and_escalation_notify_the_assignee_who_can_read_them` |
| The SLA badge says the right thing | One glance tells the story | "Response overdue" beats everything; "Due in 1h" (amber) when at risk; "Missed" for tickets resolved late | `sla.spec.ts` |

**Walkthrough found and fixed:** SQL Server warned that a connection setting (MARS) weakens rollback inside the audited
save, so it was removed from the default connection string. Audit details showed internal ids, which now show names.
Tickets created before SLA tracking had no deadlines, and now get them once at startup.

---

## Demo data

The demo data set ([demo.md](demo.md)) is checked too, so a demo never shows something the rules wouldn't produce.

| Promise | Why it matters | Scenario | Check |
|---|---|---|---|
| The demo accounts work as described | A presenter can rely on the guide | Sara, Omar and Karim sign in with the demo password; deactivated Hana is refused | `DemoDataTests.Demo_accounts_sign_in_with_the_demo_password_and_the_deactivated_one_is_refused` |
| Every state can be shown | The whole workflow and SLA can be demonstrated | The tickets cover every status and every SLA state, including a rule escalation and an unassigned queue; there are attachments and a lockout in the audit log | `Tickets_cover_every_sla_state_and_status` |
| No alert storm after a reset | The history must look like the rules really ran | Running the rule engine right after seeding fires exactly one rule, on the urgent ticket left for the demo | `Rules_already_ran_where_they_match_so_the_monitor_only_picks_up_the_ticket_left_for_the_demo` |

---

## Repeating the browser walkthrough by hand

Start the API and the web application (see the README), sign in as the administrator, then:

1. **Access:** open `/users` while signed out → you're sent to sign-in. A wrong password shows "Invalid email or password".
2. **Roles:** create a role, open its permissions, switch *View users* on and the *Customers* module on, and check the counter.
   *Administrator* shows every switch locked.
3. **Users:** add a user with that role. Saving empty shows every field's message; a duplicate email is refused.
4. **Customers:** add a company. Choosing WhatsApp without a phone shows the channel message. Log a call, add a
   note, upload a PDF and add a primary contact; the tab counters update.
5. **Tickets:** from the customer's *Tickets* tab, open an Urgent ticket. Assign it (it becomes *Open*), start work,
   comment using a quick reply, escalate with a reason, and resolve: the note is required, and the escalation clears.
   The SLA card shows due times and met or missed.
6. **Dashboard:** tiles open filtered lists; *Take* moves a queue ticket to *my tickets*; a task due today shows on the
   calendar badge.
7. **SLA & automation:** turn on automatic assignment, add a rule, create an unassigned ticket → it's assigned and the
   agent's bell shows a notification.
8. **Audit log:** each of the actions above appears with your name and IP. Expanding one shows before → after.
9. **Permissions in practice:** sign in as the new user. The menu shows only permitted pages, and buttons for
   actions they can't take are absent.
10. **Phone width:** narrow the window. The menu becomes a drawer and no page scrolls sideways.
