# Verification — Module 001 Identity & Access

Last verified: 2026-09-29. Backend `dotnet test`: **48 passed**. Frontend `npm run test:ci`: **19 passed**.
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
