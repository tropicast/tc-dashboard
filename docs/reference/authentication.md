# Authentication

ASP.NET Core Identity on the same database (`users`, `user_claims`,
`user_logins`, `user_tokens`), endpoints under `/api/v1/auth`:

- **SPA:** `POST /login` sets the session cookie `__Host-tropicast`
  (HttpOnly, Secure, SameSite=Strict, 14 days sliding). Unsafe requests that
  carry it must send the antiforgery token. `GET /antiforgery` sets the
  readable `XSRF-TOKEN` cookie, and the client echoes it in `X-XSRF-TOKEN`
  (`web/src/api/client.ts` does this). Fetch a new token after signing in.
  Local HTTP Compose explicitly uses non-secure, non-`__Host-` cookie names;
  production retains the secure defaults.
- **Desktop app:** signs in with the device authorization flow
  (`/device/code`, the user approves the code on the web, `/device/token`;
  [ADR 0001](../adr/0001-desktop-sign-in.md)), or with `POST /token` (email
  and password, for scripts). Both return a 15-minute bearer access token and a
  refresh token for one named device. `POST /token/refresh` rotates the pair;
  presenting an old refresh token again ends that device's session.
  `POST /token/revoke` or `DELETE /devices/{id}` signs one device out on its
  next refresh, and revokes the broadcast credentials it got for itself. A
  password change or reset signs every device out. The desktop contract
  (`/me/stations`, `/stations/{id}/broadcast-target`) is in
  [desktop-api.md](../desktop-api.md).
- **Accounts:** sign up, email confirmation (required before signing in),
  password reset and change, lockout after 5 failures (15 minutes), and
  passwords of at least 12 characters. Credential endpoints are rate limited
  per IP (`RateLimits:Auth`, default 10 per minute). Answers do not reveal
  whether an email address has an account.
- **Tenants:** requests choose a tenant with the `X-Tenant-Id` header
  (optional when the user has one membership). Endpoints authorize with
  `TenantPolicies`:

    | Policy                  | Roles                     |
    | ----------------------- | ------------------------- |
    | `Member`, `Broadcaster` | Owner, Admin, Broadcaster |
    | `Admin`                 | Owner, Admin              |
    | `Owner`                 | Owner                     |

- **Invitations:** `POST /api/v1/tenants/current/invitations` (Admin; only an
  Owner can invite an Owner) emails a 7-day link.
  `POST /api/v1/invitations/accept` joins with the invited, confirmed address.
- **Email:** the `IEmailSender` port with an SMTP adapter (`Email` section;
  Brevo or Resend SMTP in production). Compose runs Mailpit, which catches
  every email at http://localhost:8025.
- **Logs:** passwords, tokens and email codes are never logged. A test runs
  every flow at Trace level and checks this.
