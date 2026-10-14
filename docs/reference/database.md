# Database

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
