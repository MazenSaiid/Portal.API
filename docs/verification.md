# Verification — Module 001 Identity & Access

Last verified: 2026-09-29. Backend `dotnet test`: **109 passed**. Frontend `npm run test:ci`: **44 passed**.
Manual end-to-end run: all steps below passed against SQL Server with the Angular dev server.

## Acceptance criteria → evidence

| AC | Criterion | Automated evidence |
|---|---|---|
| AC1 | Valid login returns JWT, role, permissions | `AuthTests.Login_with_valid_credentials_returns_token_role_and_permissions` |
| AC2 | Wrong password → generic 401 | `AuthTests.Login_with_bad_credentials_returns_401_with_generic_message` (existing and unknown email) |
| AC3 | Inactive users cannot sign in | `AuthTests.Inactive_user_cannot_sign_in` |
| AC4 | No token → 401 | `AuthTests.Protected_endpoints_without_token_return_401` |
| AC5 | Missing permission → 403 | `PermissionEnforcementTests.User_without_permission_gets_403` |
| AC6/AC7 | Grant/revoke applies on next request | `PermissionEnforcementTests.Granting_and_revoking_a_permission_takes_effect_on_the_next_request` |
| AC8 | UI hides/guards by permission | `guards.spec.ts`, `has-permission.spec.ts`, manual step 9 |
| AC9 | List/search/filter/sort/page, CRUD | `UsersTests.List_supports_search_role_filter_and_paging`, `Update_then_delete_user`, `Reset_password_...` |
| AC10 | 400 with per-field errors | `UsersTests.Create_with_invalid_payload_returns_400_with_field_errors`, `form-errors.spec.ts` |
| AC11 | Duplicate email → 409 | `UsersTests.Create_with_duplicate_email_returns_409_case_insensitively` |
| AC12 | R3, R4 | `UsersTests.Admin_cannot_lock_themselves_out` (×3), `Last_active_administrator_cannot_be_deactivated_by_another_user` |
| AC13 | Role CRUD with R2, R5, R6 | `RolesTests.Duplicate_role_name_returns_409`, `System_role_cannot_be_...`, `Role_with_users_cannot_be_deleted_...` |
| AC14 | Grouped catalogue + module toggle | `RolesTests.Role_permissions_list_the_full_catalogue_grouped_by_module`, `Granting_a_whole_module_then_revoking_it`, `role-permissions.spec.ts` |
| AC15 | Administrator read-only | `RolesTests.System_role_...`, manual step 8 |
| AC16/17 | Unified design, feedback | Design tokens + shared UI kit; manual steps 3–7 |

Other covered cases: lockout after 5 failures, change password, unknown role id (400), unknown permission id (R8),
deactivated user loses access with a still-valid token (R7), role change applies immediately, "any of" permission policy,
permission registry consistency, validator edge cases, cache invalidation, paging clamps, optimistic toggle rollback.

**Checking the tests themselves:** commenting out the permission-cache invalidation in `RoleService.SetPermissionsAsync`
makes the AC6/AC7 test fail, so the suite catches that regression.

## Manual end-to-end checklist

Run the API and the frontend (see README), then:

1. Open `http://localhost:4200/users` signed out → redirected to `/login?returnUrl=%2Fusers`.
2. Sign in with a wrong password → inline "Invalid email or password."
3. Sign in as admin → lands on **Users**.
4. **Add user** → submit empty → every required field shows its error. Fill in, role *Agent*, save → success toast, row appears.
5. Add another user with the same email in upper case → "already exists" toast.
6. Search "sara" → only the matching row.
7. **Roles & permissions → Agent → Manage permissions** → toggle *View users* (toast, counter updates), toggle the *Roles* module switch → all five on.
8. Open **Administrator** permissions → info banner, every toggle disabled.
9. Sign out, sign in as the agent → the menu shows only permitted items, the Users page has no *Add/Edit/Delete* buttons, and `/users` API calls work while `DELETE` would be 403.
10. Narrow the window to phone width → the sidebar becomes a drawer and the table scrolls horizontally.

# Verification — Module 002 Refresh tokens

| AC | Criterion | Evidence |
|---|---|---|
| RT1/RT2 | Login returns a pair; refresh rotates it | `RefreshTokenTests.Refresh_rotates_the_token_pair_and_the_new_access_token_works` |
| RT3 | Reuse of a rotated token kills all sessions | `RefreshTokenTests.Reusing_a_rotated_token_is_rejected_and_revokes_the_newer_one_too` |
| RT4 | Logout revokes | `RefreshTokenTests.Logout_revokes_the_refresh_token_and_is_idempotent` |
| RT5 | Deactivation / admin reset end sessions | `Deactivated_user_cannot_refresh`, `Admin_password_reset_ends_the_users_sessions` |
| RT6 | Change password → new session, old revoked | `Change_password_returns_a_new_session_and_revokes_the_old_one` |
| RT7 | UI renewal, retry, single flight, tab sync | `auth.service.spec.ts`, `auth.interceptor.spec.ts` |

A test caught a real bug during implementation: tokens revoked by a password change were treated as "reuse", which
killed the new session too. Reuse detection now applies only to rotated tokens (spec B4).

**Live browser check** (API run with `Jwt__ExpiryMinutes=1`): the token renewed silently while the user stayed signed in.
A reload with an expired or invalid access token kept the user on `/users`. After sign-out the old refresh token returned 401.

# Verification — Module 003 Customer management

| AC | Criterion | Evidence |
|---|---|---|
| CM1 | CRUD + field errors (CR1, CR2) | `CustomersTests.Create_read_update_delete_customer`, `Invalid_customer_returns_field_errors`, `Customer_without_email_and_phone_is_rejected`, `Preferred_channel_must_be_reachable`; `CustomerRuleTests`; `customer-form-dialog.spec.ts` |
| CM2 | Duplicate email → 409 | `Duplicate_email_is_rejected_case_insensitively_but_keeping_your_own_is_fine` |
| CM3 | Search (incl. code), filters, sort, paging, last interaction | `List_supports_search_by_name_and_code_filters_and_paging`, `List_shows_last_interaction_date` |
| CM4 | Contacts with single primary | `Contacts_can_be_managed_with_a_single_primary` |
| CM5 | Interactions logged, newest first, immutable, no future dates | `Interactions_are_logged_and_listed_newest_first`, `Interaction_in_the_future_is_rejected`, `Interactions_cannot_be_edited_or_deleted` |
| CM6 | Notes, author-or-editor rule | `Only_the_author_or_an_editor_can_change_a_note`, `customer-notes.spec.ts` |
| CM7 | Files within limits, safe names and types | `Upload_download_and_delete_an_attachment`, `Disallowed_or_empty_files_are_rejected`, `Files_over_10_MB_are_rejected`, `Deleting_a_customer_removes_its_files_from_storage`, `customer-attachments.spec.ts` |
| CM8 | Permission boundaries | `Customer_endpoints_require_permissions`, `Another_agent_cannot_delete_someone_elses_file` |
| CM9 | Design system | Browser walkthrough: list, form, details, tabs, mobile width (no horizontal overflow at 390 px) |

**Found by the browser walkthrough and fixed:**
1. The *Add note* button did nothing: the form used `ngSubmit` without a form directive. It now has a regression test in `customer-notes.spec.ts`.
2. On phones the details cards overflowed the screen: the grid column needed `minmax(0, 1fr)`.

# Verification — Module 004 Ticket management

| AC | Criterion | Evidence |
|---|---|---|
| TK1 | Create with all fields, code, status, history | `TicketsTests.Creating_a_ticket_persists_every_field_and_records_history`, `Creating_with_an_assignee_starts_open`, `Invalid_ticket_returns_field_errors`, `Tickets_cannot_be_opened_for_an_inactive_customer` |
| TK2 | Filters, search, sort, paging | `List_filters_by_status_priority_assignee_customer_and_escalation` (incl. severity sort) |
| TK3 | Edits recorded in history | `Editing_records_each_change_in_history` |
| TK4 | Workflow W1–W6 | `A_ticket_goes_through_its_full_life_and_every_step_is_recorded`, `Disallowed_transitions_are_rejected`, `In_progress_requires_an_assignee`, `Resolving_requires_a_resolution_comment`, `Closed_tickets_are_read_only_until_reopened`; `TicketWorkflowRuleTests`; `ticket-details.spec.ts` |
| TK5 | Assignment A1–A3 | `Agent_can_take_an_unassigned_ticket_and_release_it_but_not_assign_others`, `Only_active_agents_can_be_assigned_and_are_listed`; `ticket-details.spec.ts` |
| TK6 | Escalation E1–E4 | `Escalation_raises_priority_and_de_escalation_keeps_it`, `Escalation_never_lowers_priority`, `Resolved_tickets_cannot_be_escalated`, `Resolving_clears_the_escalation` |
| TK7 | Comments + ordered history | Full-life test (exact event order), `ticket-labels.spec.ts` (timeline wording) |
| TK8 | Categories | `Categories_are_unique_and_protected_while_in_use` |
| TK9 | Permissions, customer tab, K7 | `Ticket_endpoints_require_permissions`, `Delete_ticket_and_customers_with_tickets_are_protected`; browser walkthrough |

**Found during implementation and fixed:**
1. A category test only passed because another test had already put "General" in use. It now creates its own ticket first.
2. `*appHasPermission="…; else …"` isn't supported by the directive and failed at runtime; the frontend test caught it.
3. Browser walkthrough: resolved tickets still showed the red *Escalated* marker, so rule E4 was added and tested. The
   assignee workload count didn't refresh after assigning, and the customer page now opens on its *Tickets* tab.
