# Privacy and data retention

The legal texts, cookie notice and data-subject rights of the dashboard
(#94). The texts are in the SPA, at `/privacy` and `/terms`.

## Legal texts

| File                       | What                                                                        |
| -------------------------- | --------------------------------------------------------------------------- |
| `web/src/legal/content.ts` | Privacy Policy, Terms of Service and cookie purposes, in English and French |
| `web/src/legal/legal.json` | Terms version, `draft` flag, and every cookie and local storage entry       |
| `src/…/Api/Auth/Legal.cs`  | `TermsVersion`: must equal `legal.json` (`AccountTests` checks it)          |

The texts are **drafts**: the pages show a "Draft" banner while `draft` is
`true`. Before publishing, the owner completes the bracketed operator details in
`content.ts` (legal name, address, registration number, privacy contact),
checks the processor list and retention periods below, and has the texts
reviewed, ideally by a lawyer. Then set `draft` to `false`.

To publish a new version, edit the texts and change the version in both
`legal.json` and `Legal.TermsVersion` (a date, `YYYY-MM-DD`). Every signed-in
user is then asked to accept it before using the dashboard.

## Cookies and browser storage

Only strictly necessary storage, so no consent banner (ePrivacy art. 5(3),
CNIL guidelines): the session cookie, the two antiforgery cookies, and the
selected workspace, language and theme in local storage. Public station pages
and the embed set none. `AccountTests.Every_cookie_the_dashboard_sets_is_listed_in_the_cookie_notice`
fails when the API sets a cookie that `legal.json` does not list.

Adding analytics or any other non-essential cookie or script needs a consent
banner first: accept and refuse equally prominent, nothing loaded before
consent, choice stored and revocable from the footer. Prefer a cookieless,
EU-hosted tool.

## Retention

| Data                                       | Kept                                                               | Enforced by                                                   |
| ------------------------------------------ | ------------------------------------------------------------------ | ------------------------------------------------------------- |
| Account, workspaces, stations, credentials | Until the account or workspace is deleted                          | `DELETE /api/v1/auth/account`, station and member endpoints   |
| Security events (with IP)                  | 90 days                                                            | `DeviceAuthorizationCleanup` (`SecurityEvent.Retention`)      |
| Desktop device sessions                    | Expire 60 days after last use; rows kept until account deletion    | `DeviceSession.Lifetime`, cascade on account deletion         |
| Audit log                                  | As long as the workspace; the actor is removed on account deletion | `DELETE /api/v1/auth/account`                                 |
| Deleted stations and their history         | Kept (usage and billing records)                                   | Soft delete                                                   |
| Database backups                           | 30 days                                                            | `deploy/backup.sh` (`BACKUP_RETENTION_DAYS`)                  |
| Logs, traces, metrics, listener logs       | Grafana Cloud plan retention (14 days on the Free tier)            | Grafana Cloud: **check the plan** before publishing the texts |
| Rate-limit counters (IP)                   | 1 minute, in memory                                                | ASP.NET Core rate limiter                                     |
| Invoices and payment data                  | Stripe, as accounting law requires                                 | Stripe                                                        |

## Account deletion

`DELETE /api/v1/auth/account` checks the password, then in one transaction:

1. **Refuses (409)** while the user is the last Owner of a workspace that still
   has other members, active stations, or a subscription that is not canceled
   (including `Incomplete`: a checkout or 3-D Secure step Stripe may still
   charge for). The message says what to do first. Workspace rows are locked in
   ID order, so concurrent deletions cannot deadlock.
2. Signs out every desktop device and revokes the broadcast credentials those
   devices got for themselves.
3. Removes the user from audit entries (`actor_user_id = NULL`) and records
   `account.deleted` in each shared workspace.
4. Deletes the workspaces they alone used, unless deleted stations keep a
   workspace for its records (it stays, with no member). Invitations they sent
   stay valid, with `invited_by = NULL`.
5. Deletes the user. Memberships, device sessions and requests, security
   events and Identity rows cascade. The browser session ends.

Every other session ends at once: the API answers 401 to any cookie or access
token whose user no longer exists (`TenantAccessMiddleware`), instead of
waiting for the cookie's security stamp check or the token's expiry.

When a new terms version is published, a user who does not accept it can still
open the Account page (download, delete) or sign out from the prompt.

Backups still hold the account for up to 30 days.
