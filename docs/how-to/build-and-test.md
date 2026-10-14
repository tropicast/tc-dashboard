# Build and test

```sh
dotnet build Tropicast.Dashboard.slnx   # also writes web/openapi/openapi.json
dotnet test --solution Tropicast.Dashboard.slnx
cd web
npm run generate:api   # regenerate src/api/schema.d.ts from openapi/openapi.json
npm run lint && npm run format:check && npm test && npm run build
```

After changing an endpoint, build the API and run `npm run generate:api`,
then commit both files: CI fails when the OpenAPI document or the generated
client is stale. In development the document is also served at
`/openapi/v1.json`.

## Browser checks

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

## Sign-up to live broadcast

`tests/e2e/run.sh` runs the whole path against the real streaming node: it
builds `tests/e2e/compose.yaml` (this API from the checkout, PostgreSQL,
Mailpit, and the tc-streaming Icecast image and exporter, no auth stub) with
fresh secrets, then runs `web/e2e/live-broadcast.spec.ts`:

1. sign up in the UI, confirm through Mailpit, create the workspace and station;
2. sign a desktop device in (`/auth/token`) and get its broadcast target;
3. publish MP3 with FFmpeg: Icecast asks source-auth, which allows it;
4. play the mount, and see the station page show Live and 1 listener;
5. refusals, each checked against the API's logged reason: bitrate above the
   plan, another station's credential, a replaced credential, a format outside
   the plan (Free narrowed to MP3 in the database, since every plan sells both),
   and a signed-out device;
6. no source password in any container log.

```sh
TC_STREAMING_DIR=../tc-streaming tests/e2e/run.sh  # needs Docker, Node, ffmpeg (libmp3lame, libopus)
```

`E2E_KEEP=1` keeps the stack for debugging. Caddy is not in the stack: the
TLS gateway is tested in tc-streaming (`tests/gateway.sh`). CI runs it in
`.github/workflows/e2e.yml` on API and SPA changes, and nightly against
tc-streaming's main.

## CI

`.github/workflows/ci.yml` runs on GitHub-hosted runners: .NET build and
tests plus the stale-client check, web format/lint/test/build, and a Docker
image build with a `/health`, SPA and version smoke test. Pull request runs are
cancelled when superseded; runs on `main` always finish.

Releases are tag-driven: pushing `v1.2.3` (or `v1.2.3-rc.1`) on `main` builds
versioned images and drafts a GitHub Release from [`CHANGELOG.md`](../../CHANGELOG.md).
The tag is the only place the version is set; the repository holds a `0.0.0`
placeholder. Steps: [Releasing](../runbook.md#releasing).
