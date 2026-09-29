# Spec 002 — Refresh tokens (session renewal)

| Item | Value |
|---|---|
| Extends | [Spec 001 — Identity & Access](001-identity-access.md) (replaces assumption A5) |
| Status | Implemented |

## 1. Goal

Users stay signed in while they keep working, without long-lived access tokens. A short access token (15 min)
is renewed silently with a refresh token (7 days). Sessions can be revoked server-side.

## 2. Assumptions

| # | Assumption | Why |
|---|---|---|
| B1 | Access token lifetime 15 minutes, refresh token lifetime 7 days (both configurable). | A stolen access token is useful only briefly, while users don't sign in every hour. |
| B2 | Refresh tokens are random 64-byte values; **only their SHA-256 hash is stored**. | A database leak does not expose usable tokens. |
| B3 | **Rotation**: every refresh revokes the used token and issues a new pair. | Limits the value of a leaked refresh token. |
| B4 | **Reuse detection**: presenting an already-*rotated* token revokes *all* of that user's sessions. A token revoked by logout, password change or deactivation is just rejected, so other devices signing in again don't kill the new session. | A reused token means two parties hold it, so one of them is an attacker. |
| B5 | The SPA keeps tokens in `localStorage` and syncs them across tabs. | The API and SPA are on different origins; httpOnly cookies would need extra CSRF work. Short access tokens and rotation limit the XSS impact. Can move to cookies later without changing the API contract much. |
| B6 | Refresh is refused for inactive or locked-out users. | Deactivation must end sessions. |

## 3. Rules

| # | Rule | Result |
|---|---|---|
| S1 | Unknown, expired or revoked refresh token | 401 |
| S2 | Reuse of a rotated token revokes every active session of the user | 401 |
| S3 | Logout revokes the presented refresh token | 204 (idempotent) |
| S4 | Deactivating a user, an admin password reset, and deleting a user revoke all of the user's sessions | — |
| S5 | Changing your own password revokes all sessions and returns a new pair for the current one | 200 + new tokens |

## 4. API changes

| Method | Route | Auth | Body → Response |
|---|---|---|---|
| POST | `/api/auth/login` | anonymous | response adds `refreshToken`, `refreshTokenExpiresAt` |
| POST | `/api/auth/refresh` | anonymous, rate-limited | `{ refreshToken }` → same shape as login |
| POST | `/api/auth/logout` | anonymous | `{ refreshToken }` → 204 |
| POST | `/api/auth/change-password` | authenticated | now returns the login shape (new session) |

## 5. Acceptance criteria

- [x] RT1 — Login returns an access token and a refresh token.
- [x] RT2 — Refresh returns a new pair; the old refresh token no longer works.
- [x] RT3 — Reusing a rotated token returns 401 and also kills the newer token (S2).
- [x] RT4 — After logout the refresh token is rejected.
- [x] RT5 — A deactivated user cannot refresh; an admin password reset ends the user's sessions.
- [x] RT6 — Change password returns a working new session and invalidates the old refresh token.
- [x] RT7 — The UI renews the access token before it expires and on a 401, retries the failed request once,
      and signs the user out only when renewal fails. Concurrent requests share a single refresh call.
