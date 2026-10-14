# Observability and security

- **Health**: `/health/live` (the process answers) and `/health/ready` (the
  database answers). Deploys wait for `ready`. `/health` remains as `live`.
- **Logs**: JSON lines in production. Every value passes a redaction filter
  (`Api/Logging/Redaction.cs`: bearer and Basic credentials, `password=`,
  URL credentials, refresh tokens, 256-bit secrets), and stack traces are not
  shipped. `SecretLoggingTests` runs every credential flow at Trace level and
  checks no secret reaches any log.
- **Telemetry**: OpenTelemetry traces (ASP.NET Core, HttpClient, Npgsql) and
  metrics (runtime, HTTP, and meter `Tropicast.Dashboard`: `tropicast.up`,
  source-auth decisions and latency, provisioning, stats and directory
  results) are exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
  In production they go to Alloy, then Grafana Cloud. Alert rules are in
  `monitoring/rules.yaml`, unit-tested with promtool.
- **Rate limits** per client address: sign-in and credential endpoints
  (10/min), public pages and API (120/min), desktop sign-in polling
  (120/min), and source auth (1,200/min).
- **Headers**: HSTS, a Content-Security-Policy for the SPA (`'self'` only),
  Permissions-Policy and COOP, all set by Caddy. Public pages keep their own
  nonce-based policy.
- **Data Protection keys** in the database: cookies and tokens survive
  redeploys and are shared by every API instance.
- **Audit**: the tenant audit log (`GET /api/v1/tenants/current/audit`,
  Admin) records stations, credentials, members and plan changes. Each user's
  account activity (`GET /api/v1/auth/activity`) lists sign-ins, failed
  sign-ins, device sign-ins and sign-outs, refresh-token reuse and password
  changes, kept for 90 days.
- **Tenant isolation**: `TenantIsolationTests` finds every tenant-scoped
  endpoint in the routing table. It calls each one as another tenant's member
  with tenant A's IDs, and checks there is no success and no data from A.
- **CI scanning**: vulnerable NuGet packages (including transitive ones) and
  production npm packages fail the build, and so does Trivy for high or
  critical fixable issues in the image.
