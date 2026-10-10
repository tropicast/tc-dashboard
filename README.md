# Tropicast dashboard

Control plane for Tropicast: REST API, PostgreSQL and the React station
dashboard. Station owners create an account, manage stations, get broadcast
credentials for the desktop app and see live status. Audio never passes
through this service; it is served by
[tc-streaming](https://github.com/tropicast/tc-streaming). Epic:
[#1](https://github.com/tropicast/tc-dashboard/issues/1).

## Layout

| Path                                     | Contents                                                                         |
| ---------------------------------------- | -------------------------------------------------------------------------------- |
| `src/Tropicast.Dashboard.Domain`         | Entities, value objects, domain errors. No framework references                  |
| `src/Tropicast.Dashboard.Application`    | Use cases, ports (`IClock`, …), validation (FluentValidation)                    |
| `src/Tropicast.Dashboard.Infrastructure` | Adapters for the ports: EF Core, Identity, email, background services            |
| `src/Tropicast.Dashboard.Api`            | Minimal API under `/api/v1`, OpenAPI, Problem Details, `/health`; serves the SPA |
| `web/`                                   | React 19 + TypeScript + Vite SPA, typed API client generated from OpenAPI        |
| `tests/`                                 | Domain, Application, architecture and API integration tests                      |

Dependencies point inward (Domain ← Application ← Infrastructure ← Api).
`tests/Tropicast.Dashboard.Architecture.Tests` fails the build when an inner
layer references an outer one, or EF Core / ASP.NET Core.

## Prerequisites

- .NET 10 SDK **with the ASP.NET Core targeting pack** (the Microsoft
  installers include it; on Arch Linux also install `aspnet-targeting-pack`)
- Node.js 24 and npm
- Docker with Compose, for PostgreSQL and the image

## Run

Step by step, with a walkthrough from sign-up to a station credential:
[docs/how-to/run-locally.md](docs/how-to/run-locally.md).

```sh
docker compose up --build          # PostgreSQL 17 + API serving the SPA: http://localhost:8080
docker compose --profile dev up    # plus the Vite dev server with hot reload: http://localhost:5173
```

Without Docker for the API:

```sh
dotnet run --project src/Tropicast.Dashboard.Api   # http://localhost:5080
cd web && npm ci && npm run dev                      # proxies /api to :5080
```

## Build and test

```sh
dotnet build Tropicast.Dashboard.slnx   # also writes web/openapi/openapi.json
dotnet test Tropicast.Dashboard.slnx
cd web
npm run generate:api   # regenerate src/api/schema.d.ts from openapi/openapi.json
npm run lint && npm run format:check && npm test && npm run build
```

After changing an endpoint, build the API and run `npm run generate:api`,
then commit both files: CI fails when the OpenAPI document or the generated
client is stale. In development the document is also served at
`/openapi/v1.json`.

### Browser checks

```sh
cd web
npm run test:e2e:install  # install Chromium once
npm run test:e2e           # SPA E2E: mocked API contract responses only
npm run test:lighthouse    # accessibility score >= 95 for sign-in and sign-up routes
```

The Playwright smoke test exercises browser navigation and client request
payloads while intercepting `/api/v1` responses. It does **not** prove API or
PostgreSQL behavior; .NET integration tests own that coverage.

For the opt-in real browser/API/PostgreSQL journey, start Compose first, then
run the dedicated command:

```sh
docker compose up --build -d  # API at http://localhost:8080; Mailpit at http://localhost:8025
cd web
npm run test:e2e:real
```

This test creates a unique account, tenant, and station; it retrieves the
confirmation link from Mailpit and leaves this test data in the local Compose
database. Override the dashboard URL with `REAL_E2E_BASE_URL`; override
Mailpit with `REAL_E2E_MAILPIT_URL`.

## Database

EF Core 10 + Npgsql, PostgreSQL 17, snake_case names, `timestamptz`
timestamps, optimistic concurrency on `xmin`. Code-first migrations live in
`src/Tropicast.Dashboard.Infrastructure/Persistence/Migrations`.

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/Tropicast.Dashboard.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations bundle --project src/Tropicast.Dashboard.Infrastructure --self-contained -r linux-x64 -o efbundle
./efbundle --connection "<connection string>"   # how deploys migrate
```

The API never migrates in production. `docker compose up` sets
`Database__MigrateOnStartup=true`, which is honoured only in Development.
Only the plan catalogue is seeded.

Two named query filters protect the default query path: `tenant` returns
only the current tenant's rows (none when no tenant is set), and `deleted`
hides soft-deleted stations. Their sessions, stats and credentials stay in the
database. Cross them explicitly with
`IgnoreQueryFilters([AppDbContext.TenantFilter])` or `[AppDbContext.DeletedFilter]`.

`tests/Tropicast.Dashboard.Infrastructure.Tests` runs against PostgreSQL 17
in Docker (Testcontainers).

## Authentication

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
  [ADR 0001](docs/adr/0001-desktop-sign-in.md)), or with `POST /token` (email
  and password, for scripts). Both return a 15-minute bearer access token and a
  refresh token for one named device. `POST /token/refresh` rotates the pair;
  presenting an old refresh token again ends that device's session.
  `POST /token/revoke` or `DELETE /devices/{id}` signs one device out on its
  next refresh, and revokes the broadcast credentials it got for itself. A
  password change or reset signs every device out. The desktop contract
  (`/me/stations`, `/stations/{id}/broadcast-target`) is in
  [docs/desktop-api.md](docs/desktop-api.md).
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

## REST API

All under `/api/v1`. Errors are RFC 9457 Problem Details, with field errors for
validation (FluentValidation in Application). In Development, the OpenAPI
document is at `/openapi/v1.json`, and an interactive reference with examples
is at `/scalar`.

| Endpoint                                           | Policy    | Notes                                                                                               |
| -------------------------------------------------- | --------- | --------------------------------------------------------------------------------------------------- |
| `POST /tenants`                                    | signed in | Creates a tenant on the Free plan; the caller is Owner                                              |
| `GET /tenants/current`                             | Member    | Plan, limits and station count; `ETag`                                                              |
| `PATCH /tenants/current`                           | Admin     | Needs `If-Match`                                                                                    |
| `GET /tenants/current/members`                     | Member    |                                                                                                     |
| `DELETE /tenants/current/members/{userId}`         | Admin     | Only an Owner removes an Owner; the last Owner stays                                                |
| `POST /tenants/current/invitations`                | Admin     | How members are added                                                                               |
| `GET /stations?page=&pageSize=`                    | Member    | Paged (1-100 per page)                                                                              |
| `POST /stations`                                   | Admin     | Within the plan's station limit; assigns the stream and returns listener URLs                       |
| `GET /stations/{id}`                               | Member    | `ETag`                                                                                              |
| `PATCH /stations/{id}`                             | Admin     | Needs `If-Match` (428 without, 412 when stale)                                                      |
| `DELETE /stations/{id}`                            | Admin     | Soft delete; optional `If-Match`                                                                    |
| `GET /stations/{id}/credentials`                   | Admin     | Per-device broadcast credentials, active first; never the secret                                    |
| `POST /stations/{id}/credentials`                  | Admin     | Issues a 256-bit secret for one device; the only response that shows it (`Cache-Control: no-store`) |
| `DELETE /stations/{id}/credentials/{credentialId}` | Admin     | Revokes that device only; idempotent                                                                |

Stations of other tenants answer 404. Broadcast credentials are stored as
SHA-256 hashes; the Icecast username is the station's public ID. Credential
creation and revocation are recorded in `audit_entries` (actor, action,
target; never secrets). Listener URLs come from `Streaming`
settings (`Node`, `ListenerBaseUrl`). A station change is saved together with
an `outbox_messages` row (`StationCreated`, `StationChanged`,
`StationDeleted`). A background dispatcher hands these rows to the
`IOutboxConsumer` implementations (provisioning #8, RadioBrowser #13), at least
once, retrying failures with backoff (`Outbox` settings).

## Source auth for Icecast

`POST /internal/icecast/source-auth` implements tc-streaming's
[source-auth contract](https://github.com/tropicast/tc-streaming/blob/main/docs/source-auth.md):
Icecast asks it before accepting audio from a source.

- **Internal port only.** It is served on port 8081 (`SourceAuth:Port`) and
  answers 404 on the public port 8080, so the public gateway never routes it.
  Only the streaming node, on the private network, reaches it.
- **Node credentials.** Icecast authenticates with HTTP Basic
  (`SourceAuth:NodeUsername` / `NodePassword`, the node's
  `ICECAST_SOURCE_AUTH_USER` / `_PASSWORD`), compared in constant time. When
  they are not set, every call gets 401.
- **Allow** (`200` with `icecast-auth-user: 1`) only when all of these hold:
  the mount is `/stations/{public id}/live.(mp3|opus)`, the user is that
  station, the password matches one of its active (unrevoked) credentials,
  the tenant is active, the plan allows the format, and a declared bitrate
  (`Ice-Bitrate`, else `Ice-Audio-Info`) is within the plan.
- **Deny** returns `200` with `icecast-auth-message: <reason>`. A reason never
  contains the credential. A database timeout (2 s) or any error also denies.
- **On allow,** it records the credential's `last_used_at` and opens a
  `LiveSession`.

To try it with a real Icecast, run tc-streaming's `icecast` service with
`ICECAST_SOURCE_AUTH_URL=http://api:8081/internal/icecast/source-auth` and
matching node credentials, and attach it to this stack's network
(`docker network connect tc-dashboard_default tc-streaming-icecast-1`).

## Provisioning (station limits on the streaming node)

The database is the desired state of the streaming node. A background worker
renders it as tc-streaming's `stations.json` (one entry per station: plan,
listener cap, bitrate, formats). When its version differs from the one last
applied, the worker sends it over SSH to the node, where `deploy.sh
apply-stations` reloads Icecast with no restart.

- **When it runs:** on station create or delete and on plan changes (through
  the outbox, within seconds), and at least every 30 s
  (`Provisioning:Interval`).
- **Failures:** retried with exponential backoff from 15 s, up to 10 min.
  They never block the dashboard. The operator view
  (`GET /api/v1/operator/streaming-nodes`) shows desired and applied versions,
  failures and the next try. `POST …/{node}/reconcile` retries at once.
  Operators are the accounts listed in `Operators:Emails`.
- **One applier:** a PostgreSQL advisory lock keeps it to one API instance at
  a time.
- **Capacity guard:** creating a station returns 503, and logs a critical
  "node is full" error, when the node would exceed Icecast's `<sources>`
  (`Streaming:MaxSources`, 50; every allowed format of a station counts) or
  the oversubscribed sum of listener caps (`Streaming:MaxListenerCaps`,
  15,000).

Production settings (`Provisioning:Ssh`):

| Setting         | Value                                                                                                                                                                                   |
| --------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Host`, `Port`  | The streaming node over the private network, `22`                                                                                                                                       |
| `Username`      | `deploy`                                                                                                                                                                                |
| `PrivateKey`    | An ed25519 key used only for this (secret). On the node, its public key is installed with the forced command `deploy.sh apply-stations` (tc-streaming Ansible `provisioning_ssh_keys`). |
| `HostKeySha256` | The node's ed25519 host key: `ssh-keyscan -t ed25519 <host> \| ssh-keygen -lf -`                                                                                                        |

Compose sets `Provisioning__Enabled=false`: locally there is no node.

## CI

`.github/workflows/ci.yml` runs on GitHub-hosted runners: .NET build and
tests plus the stale-client check, web format/lint/test/build, and a Docker
image build with a `/health` and SPA smoke test. Pull request runs are
cancelled when superseded; runs on `main` always finish.

## Production

One Hetzner CX23 (`tc-app-1`) runs the API and Caddy for
`app.tropicastradio.com`. It shares a private network with the streaming
node, which calls source auth over it. The database is Neon PostgreSQL, with
nightly encrypted dumps to object storage, a daily backup check, and a monthly
restore test run offline.

| Path                           | What                                                                                      |
| ------------------------------ | ----------------------------------------------------------------------------------------- |
| `infra/terraform`              | server, firewall, primary IPs, private network, Cloudflare DNS                            |
| `infra/ansible`                | hardening, Docker, deploy user, backup timer                                              |
| `deploy/`                      | production Compose file, Caddyfile, `deploy.sh`, `backup.sh`, `restore-test.sh`           |
| `.github/workflows/image.yml`  | images `ghcr.io/tropicast/dashboard{,-migrations}:sha-<commit>` for each commit on `main` |
| `.github/workflows/deploy.yml` | manual deploy: migrate, swap, health check, automatic return to the running release       |

Setup, deploy, rollback, restore and secret rotation:
[docs/runbook.md](docs/runbook.md).

## License

Copyright 2026 Tropicast. Licensed under the [Apache License, Version 2.0](LICENSE);
see [NOTICE](NOTICE). The license does not grant use of the Tropicast name or
logo (section 6).

## Responsible use

- The software is provided "AS IS", without warranties or conditions of any
  kind. The authors and Tropicast are not liable for any damages or claims
  arising from its use (Apache License 2.0, sections 7 and 8).
- You are responsible for what you broadcast or host with it: rights and
  royalties for music and other content, and the broadcasting, privacy and
  other laws that apply to you and your listeners.
- Do not use it to distribute content you have no right to distribute, or for
  any unlawful purpose.
