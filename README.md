# Tropicast dashboard

Control plane for Tropicast: REST API, PostgreSQL and the React station
dashboard. Station owners create an account, manage stations, get broadcast
credentials for the desktop app and see live status. Audio never passes
through this service; it is served by
[tc-streaming](https://github.com/tropicast/tc-streaming). Epic:
[#1](https://github.com/tropicast/tc-dashboard/issues/1).

## Layout

| Path | Contents |
|---|---|
| `src/Tropicast.Dashboard.Domain` | Entities, value objects, domain errors. No framework references |
| `src/Tropicast.Dashboard.Application` | Use cases, ports (`IClock`, …), validation (FluentValidation) |
| `src/Tropicast.Dashboard.Infrastructure` | Adapters for the ports: EF Core, Identity, email, background services |
| `src/Tropicast.Dashboard.Api` | Minimal API under `/api/v1`, OpenAPI, Problem Details, `/health`; serves the SPA |
| `web/` | React 19 + TypeScript + Vite SPA, typed API client generated from OpenAPI |
| `tests/` | Domain, Application, architecture and API integration tests |

Dependencies point inward (Domain ← Application ← Infrastructure ← Api).
`tests/Tropicast.Dashboard.Architecture.Tests` fails the build when an inner
layer references an outer one, or EF Core / ASP.NET Core.

## Prerequisites

- .NET 10 SDK **with the ASP.NET Core targeting pack** (the Microsoft
  installers include it; on Arch Linux also install `aspnet-targeting-pack`)
- Node.js 24 and npm
- Docker with Compose, for PostgreSQL and the image

## Run

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

## CI

`.github/workflows/ci.yml` runs on GitHub-hosted runners: .NET build and
tests plus the stale-client check, web format/lint/test/build, and a Docker
image build with a `/health` and SPA smoke test. Pull request runs are
cancelled when superseded; runs on `main` always finish.

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
