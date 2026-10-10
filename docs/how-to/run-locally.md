# Run the dashboard locally with Docker Compose

`compose.yaml` runs the whole control plane on your machine:

| Service | What | Address |
|---|---|---|
| `api` | ASP.NET Core API, also serving the built React SPA | http://localhost:8080 |
| `db` | PostgreSQL 17 | `localhost:5432` (database, user: `tropicast`) |
| `mail` | Mailpit: catches every email, delivers nothing | http://localhost:8025 |
| `web` (profile `dev`) | Vite dev server with hot reload, proxying `/api` to `api` | http://localhost:5173 |

The API runs in `Development`: it applies migrations on start, serves the
OpenAPI document at `/openapi/v1.json` and the API reference at `/scalar/`.

## Prerequisites

- Docker with Compose v2 (`docker compose version`).
- Free ports 8080, 8025, 5432 (and 5173 for the `dev` profile). To change
  them, see [Ports and settings](#ports-and-settings).
- For the walkthrough below: `curl` and `jq`.

You do not need .NET or Node.js on the host; the images build everything.

## Start

```sh
docker compose up -d --build
docker compose ps                        # api, db and mail: Up
curl -s http://localhost:8080/health     # Healthy
```

- Open http://localhost:8080 for the SPA.
- Open http://localhost:8080/scalar/ to try the API.
- Open http://localhost:8025 to read the emails it sends.

To work on the SPA with hot reload, also start the dev server:

```sh
docker compose --profile dev up -d
```

Then open http://localhost:5173. Edits under `web/src` reload at once; API
calls go to the `api` container.

## Walkthrough: account, tenant, station, credential

The SPA has no sign-in pages yet (#11), so this uses the API directly. It
signs in like the desktop app does, with a bearer token, so no cookie or
antiforgery token is needed.

```sh
API=http://localhost:8080
EMAIL=dj@example.test
PASSWORD='a long local passphrase'     # at least 12 characters

# 1. Sign up. The confirmation email lands in Mailpit.
curl -s -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" $API/api/v1/auth/register

# 2. Confirm with the link from the email (also visible at http://localhost:8025).
LINK=$(curl -s http://localhost:8025/api/v1/message/latest | jq -r '.Text' | grep -o 'http[^[:space:]]*confirm-email[^[:space:]]*')
USER_ID=$(echo "$LINK" | sed -E 's/.*userId=([^&]+).*/\1/')
CODE=$(echo "$LINK" | sed -E 's/.*code=([^&]+).*/\1/')
curl -s -H 'Content-Type: application/json' \
  -d "{\"userId\":\"$USER_ID\",\"code\":\"$CODE\"}" $API/api/v1/auth/confirm-email

# 3. Sign in as a device: a 15-minute access token.
TOKEN=$(curl -s -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"deviceName\":\"curl\"}" \
  $API/api/v1/auth/token | jq -r .accessToken)
AUTH="Authorization: Bearer $TOKEN"

# 4. Create a tenant; you become its Owner. Later calls select it with X-Tenant-Id.
TENANT=$(curl -s -H "$AUTH" -H 'Content-Type: application/json' \
  -d '{"name":"My radio group","slug":"my-radios"}' $API/api/v1/tenants | jq -r .id)

# 5. Create a station: it gets a public ID and listener URLs.
STATION=$(curl -s -H "$AUTH" -H "X-Tenant-Id: $TENANT" -H 'Content-Type: application/json' \
  -d '{"name":"Radio Mada","slug":"radio-mada"}' $API/api/v1/stations | jq -r .id)
curl -s -H "$AUTH" -H "X-Tenant-Id: $TENANT" $API/api/v1/stations/$STATION | jq .listenerUrls

# 6. Issue a broadcast credential for a device. The secret is shown only here.
curl -s -H "$AUTH" -H "X-Tenant-Id: $TENANT" -H 'Content-Type: application/json' \
  -d '{"deviceLabel":"Studio PC"}' $API/api/v1/stations/$STATION/credentials | jq '{username, secret}'
```

The listener URLs point to production (`listen.tropicastradio.com`); there is
no Icecast in this stack. To publish audio locally, run the
[tc-streaming](https://github.com/tropicast/tc-streaming) stack as well.

## Daily use

```sh
docker compose up -d --build        # after pulling or changing backend code
docker compose logs -f api          # API logs
docker compose exec db psql -U tropicast -d tropicast    # SQL shell
docker compose stop                 # stop, keep data
docker compose down                 # remove containers, keep data
docker compose down -v              # remove containers and data (fresh database)
```

New migrations apply when the `api` container starts. Production never does
this: deploys run the migration bundle instead.

## Ports and settings

Each port can be changed with an environment variable, in the shell or in a
`.env` file next to `compose.yaml`:

| Variable | Default | For |
|---|---|---|
| `API_PORT` | 8080 | API and SPA (also used in email links) |
| `DB_PORT` | 5432 | PostgreSQL |
| `MAIL_UI_PORT` | 8025 | Mailpit web UI |
| `WEB_PORT` | 5173 | Vite dev server (`dev` profile) |
| `POSTGRES_PASSWORD` | `tropicast-dev` | Database password |

```sh
API_PORT=18080 DB_PORT=15432 docker compose up -d
```

Everything binds to `127.0.0.1` only, so the stack is not reachable from other
machines.

## Troubleshooting

- **`port is already allocated`.** Another program uses the port: pick
  another one (see above).
- **The API restarts with `relation "…" already exists`.** Your local database
  was migrated with migrations that have since changed. Start fresh with
  `docker compose down -v && docker compose up -d --build`.
- **No email in Mailpit.** Check `docker compose logs api`. Registering an
  address that already has an account sends an "account exists" email
  instead of a confirmation.
- **Signing in from the SPA over plain HTTP.** The session cookie is `Secure`.
  Browsers accept it on `http://localhost`, but not on another host name or
  IP address: use `localhost`.
- **`429 Too Many Requests` on sign-in.** The credential endpoints allow 10
  requests per minute per client. Wait a minute.
