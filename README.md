<p align="center">
  <img src="web/public/tropicast-logo.svg" alt="Tropicast logo" width="120" height="120">
</p>

<h1 align="center">Tropicast Dashboard</h1>

<p align="center">
  Control plane for Tropicast: accounts, stations, billing, desktop sign-in and Icecast source auth.
</p>

<p align="center">
  <a href="https://github.com/tropicast/tc-dashboard/actions/workflows/ci.yml"><img src="https://github.com/tropicast/tc-dashboard/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI"></a>
  <a href="https://github.com/tropicast/tc-dashboard/actions/workflows/e2e.yml"><img src="https://github.com/tropicast/tc-dashboard/actions/workflows/e2e.yml/badge.svg?branch=main" alt="E2E"></a>
  <a href="https://github.com/tropicast/tc-dashboard/actions/workflows/image.yml"><img src="https://github.com/tropicast/tc-dashboard/actions/workflows/image.yml/badge.svg?branch=main" alt="Image"></a>
  <a href="https://github.com/tropicast/tc-dashboard/releases"><img src="https://img.shields.io/github/v/release/tropicast/tc-dashboard?include_prereleases&sort=semver" alt="Release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/tropicast/tc-dashboard" alt="License: Apache-2.0"></a>
  <a href="global.json"><img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10"></a>
  <a href="web/package.json"><img src="https://img.shields.io/badge/React-19-149ECA?logo=react&logoColor=white" alt="React 19"></a>
  <a href=".github/dependabot.yml"><img src="https://img.shields.io/badge/Dependabot-enabled-025E8C?logo=dependabot&logoColor=white" alt="Dependabot enabled"></a>
</p>

A REST API, PostgreSQL and the React station dashboard. Station owners create an
account, manage stations, get broadcast credentials for the desktop app
([tc-station](https://github.com/tropicast/tc-station)) and see live status.
Audio never passes through this service; it is served by
[tc-streaming](https://github.com/tropicast/tc-streaming), which asks this API
before accepting a source. Epic: [#1](https://github.com/tropicast/tc-dashboard/issues/1).

## Features

- Accounts with email confirmation, tenants, members and invitations, with role-based access.
- Stations and per-device broadcast credentials (shown once, stored hashed, revocable).
- Desktop sign-in with the device authorization flow, and the desktop broadcast contract.
- Icecast source auth: plan, format and bitrate checks for every source that connects.
- Provisioning: station limits pushed to the streaming node with no Icecast restart.
- Live listener stats, usage (listener-hours, egress) and an audit log.
- Public station pages, an embeddable player, and RadioBrowser listing.
- Subscriptions with Stripe Checkout and the Billing Portal.
- Privacy Policy, Terms of Service and cookie notice; data export and account deletion.

## Quick start

Prerequisites: Docker with Compose. To build without Docker: the .NET 10 SDK
**with the ASP.NET Core targeting pack** (on Arch Linux, also install
`aspnet-targeting-pack`), Node.js 24 and npm.

```sh
docker compose up --build          # PostgreSQL 17 + API serving the SPA: http://localhost:8080
docker compose --profile dev up    # plus the Vite dev server with hot reload: http://localhost:5173
```

Mailpit catches every email at http://localhost:8025. Walkthrough from sign-up
to a station credential: [docs/how-to/run-locally.md](docs/how-to/run-locally.md).

Without Docker for the API:

```sh
dotnet run --project src/Tropicast.Dashboard.Api   # http://localhost:5080
cd web && npm ci && npm run dev                      # proxies /api to :5080
```

## Build and test

```sh
dotnet build Tropicast.Dashboard.slnx   # also writes web/openapi/openapi.json
dotnet test --solution Tropicast.Dashboard.slnx
cd web && npm run generate:api && npm run lint && npm run format:check && npm test && npm run build
```

After changing an endpoint, commit the regenerated `web/openapi/openapi.json`
and `web/src/api/schema.d.ts`: CI fails when they are stale. Browser checks, the
sign-up to live broadcast E2E test and CI:
[docs/how-to/build-and-test.md](docs/how-to/build-and-test.md).

## Layout

| Path                                     | Contents                                                                         |
| ---------------------------------------- | -------------------------------------------------------------------------------- |
| `src/Tropicast.Dashboard.Domain`         | Entities, value objects, domain errors. No framework references                  |
| `src/Tropicast.Dashboard.Application`    | Use cases, ports (`IClock`, …), validation (FluentValidation)                    |
| `src/Tropicast.Dashboard.Infrastructure` | Adapters for the ports: EF Core, Identity, email, background services            |
| `src/Tropicast.Dashboard.Api`            | Minimal API under `/api/v1`, OpenAPI, Problem Details, `/health`; serves the SPA |
| `web/`                                   | React 19 + TypeScript + Vite SPA, typed API client generated from OpenAPI        |
| `tests/`                                 | Domain, Application, architecture and API integration tests; E2E in `tests/e2e`  |
| `deploy/`, `infra/`                      | Production Compose file and scripts, Terraform and Ansible                       |

Dependencies point inward (Domain ← Application ← Infrastructure ← Api).
`tests/Tropicast.Dashboard.Architecture.Tests` fails the build when an inner
layer references an outer one, or EF Core / ASP.NET Core.

## Documentation

| Topic                                                                                                   | Covers                                                                    |
| ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| [Run locally](docs/how-to/run-locally.md)                                                               | Compose services, ports and settings, sign-up walkthrough                 |
| [Build and test](docs/how-to/build-and-test.md)                                                         | Tests, browser checks, sign-up to live broadcast E2E, CI, releases        |
| [Database](docs/reference/database.md)                                                                  | EF Core, migrations, tenant and soft-delete query filters                 |
| [Authentication](docs/reference/authentication.md)                                                      | SPA cookie, desktop tokens, accounts, tenant policies, invitations, email |
| [REST API](docs/reference/rest-api.md)                                                                  | Tenants, members, stations and credentials endpoints, outbox              |
| [Desktop API](docs/desktop-api.md)                                                                      | Contract used by tc-station                                               |
| [Source auth for Icecast](docs/reference/source-auth.md)                                                | Internal endpoint the streaming node calls before accepting audio         |
| [Provisioning](docs/reference/provisioning.md)                                                          | Station limits on the streaming node, capacity guard, SSH settings        |
| [Public station page and embed](docs/reference/public-pages.md)                                         | Public pages, embeddable player, public API                               |
| [RadioBrowser listing](docs/reference/radiobrowser.md)                                                  | Directory listing and updates                                             |
| [Observability and security](docs/reference/observability-and-security.md)                              | Health, logs, telemetry, rate limits, headers, audit, tenant isolation    |
| [Billing](docs/reference/billing.md)                                                                    | Stripe Checkout, Billing Portal, webhooks, plan changes                   |
| [Privacy and data retention](docs/reference/data-retention.md)                                          | Legal texts, cookies, retention periods, export and account deletion      |
| [Runbook](docs/runbook.md)                                                                              | Production setup, deploy, releases, rollback, backups, secrets, alerts    |
| ADRs: [desktop sign-in](docs/adr/0001-desktop-sign-in.md), [payments](docs/adr/0002-payments-stripe.md) | Architecture decisions                                                    |
| [Changelog](CHANGELOG.md)                                                                               | Changes per release                                                       |

## Production

One Hetzner server runs the API and Caddy for `app.tropicastradio.com`, next to
the streaming node on a private network. The database is Neon PostgreSQL, with
nightly encrypted backups. Every commit on `main` builds images; deploys are
manual, with automatic return to the running release when the new one is
unhealthy. Releases are tag-driven, and the tag is the only place the version is
set. Details: [docs/runbook.md](docs/runbook.md).

## Contributing

One issue, one branch (`feat/…`, `fix/…`, `docs/…`, `ci/…`) and one pull
request. CI must pass before review. Add user-facing changes to the
`Unreleased` section of [CHANGELOG.md](CHANGELOG.md).

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
