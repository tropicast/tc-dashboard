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

| Data                                       | Kept                                                                      | Enforced by                                                                                         |
| ------------------------------------------ | ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Account, workspaces, stations, credentials | Until the account or workspace is deleted                                 | `DELETE /api/v1/auth/account`, `DELETE /api/v1/tenants/current`, station and member endpoints       |
| Security events (with IP)                  | 90 days                                                                   | `DeviceAuthorizationCleanup` (`SecurityEvent.Retention`)                                            |
| Desktop device sessions                    | Expire 60 days after last use; rows kept until account deletion           | `DeviceSession.Lifetime`, cascade on account deletion                                               |
| Audit log                                  | As long as the workspace; the actor is removed on account deletion        | `DELETE /api/v1/auth/account`                                                                       |
| Deleted stations and their history         | Until the workspace is deleted (usage and billing records)                | Soft delete; `DELETE /api/v1/tenants/current` removes them                                          |
| Uploaded audio (AutoDJ tracks)             | Until the track or its station is deleted; unconfirmed uploads 30 minutes | Track endpoints, `StationTracksCleanup`, `PendingUploadCleanup` ([media library](media-library.md)) |
| Database backups                           | 30 days                                                                   | `deploy/backup.sh` (`BACKUP_RETENTION_DAYS`)                                                        |
| Logs, traces, metrics, listener logs       | Grafana Cloud plan retention (14 days on the Free tier)                   | Grafana Cloud: **check the plan** before publishing the texts                                       |
| Rate-limit counters (IP)                   | 1 minute, in memory                                                       | ASP.NET Core rate limiter                                                                           |
| Invoices and payment data                  | Stripe, as accounting law requires                                        | Stripe                                                                                              |

## Workspace deletion

An Owner deletes the whole workspace from the Account page (#125). The steps
are those of the operator's `ops/delete-workspace.sh`:

1. **Report**, nothing changes: `GET /api/v1/tenants/current/deletion` lists
   the subscription, the members (and who is left without a workspace), the
   stations (live, listed on RadioBrowser) and the rows that go.
2. **Guards**: refused (409) while a subscription is `Active`, `Trialing`,
   `PastDue` or `Incomplete`, or a station is live. Checked again inside the
   delete transaction, with the workspace row locked (`FOR UPDATE`).
3. **Slug**: `DELETE /api/v1/tenants/current` takes `{ "confirmSlug": "…" }`
   and answers 422 when it is not the workspace's slug.
4. **Delete**, one transaction (`WorkspaceDeletion`): the workspace's
   undelivered outbox events (except `StationDeleted`), then one
   `StationDeleted` per station still on air, then credentials, live
   sessions, stats rollups, stream assignments, directory listings, AutoDJ
   settings, encoding profiles, playlists, tracks and stations, then the
   workspace (memberships, subscriptions, invitations and audit entries
   cascade). Member accounts stay.
5. **After the commit**: the `StationDeleted` events remove the uploaded audio
   from object storage and drop the mounts at the next provisioning pass. The
   owners get a confirmation email. The deleting user's security events keep a
   `workspace.deleted` record (workspace ID, slug and counts, no member emails
   or station names) for 90 days.

Stripe keeps the invoices; the RadioBrowser entry of a listed station stays
there and goes offline. Backups still hold the workspace for up to 30 days.

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
4. Deletes the workspaces they alone used (their stations, and with them their
   uploaded audio, must already be deleted), unless deleted stations keep a
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
