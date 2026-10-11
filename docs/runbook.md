# Runbook: control-plane production

How the control plane (`app.tropicastradio.com`) is hosted, deployed, backed
up and restored. Issue #15. The streaming node has its own runbook:
[tc-streaming docs/runbook.md](https://github.com/tropicast/tc-streaming/blob/main/docs/runbook.md).

## Overview

```text
            Internet
               |  443/80
     +---------v-------------------+        private network 10.20.0.0/16
     | tc-app-1 (CX23, fsn1)       |        (Hetzner firewalls do not filter it)
     |  caddy  -> api:8080         |
     |  api:8081 on 10.20.1.10  <------------ Icecast source auth (tc-stream-1)
     |  api --SSH 10.20.1.2:22 ------------->  deploy.sh apply-stations (forced command)
     +---------+-------------------+
               | TLS
     Neon PostgreSQL (aws-eu-central-1)      Hetzner Object Storage: nightly encrypted dumps
```

| Part | Where | Code |
|---|---|---|
| Server, firewall, primary IPs, private network, DNS | Hetzner Cloud, Cloudflare | `infra/terraform` |
| Hardening, Docker, deploy user, backup timer | the node | `infra/ansible` |
| API + Caddy | `/opt/tc-dashboard` on the node | `deploy/compose.yaml`, `deploy/deploy.sh` |
| Images | `ghcr.io/tropicast/dashboard`, `dashboard-migrations` (`sha-<commit>`) | `.github/workflows/image.yml` |
| Database | Neon | connection URIs in secrets |
| Backups | `s3://<bucket>/db/*.dump.age` | `deploy/backup.sh`, `deploy/restore-test.sh` |

**Cost** (monthly, before paying customers): CX23 about €4, two primary IPs
about €1, Neon Free €0, Object Storage €5 (shared with the Terraform state),
SMTP free tier. About €10, under the $30 target. Neon Launch ($19) when the
free compute or storage is no longer enough.

## 1. Accounts and secrets (one-time, by you)

Create these, then store them in the tc-dashboard repository (**Settings →
Secrets and variables → Actions**). Never commit them.

1. **Neon**: project in `aws-eu-central-1`, PostgreSQL 17. Copy two URIs from
   *Connect*: the **pooled** one (host with `-pooler`) and the **direct** one.
2. **SMTP** (Brevo or Resend): a sending domain for `tropicastradio.com` with
   SPF and DKIM, and SMTP credentials.
3. **Backups**: an Object Storage bucket (e.g. `tropicast-backups`, `fsn1`)
   and an access key limited to it (read/write, for the node), plus a
   read-only key (for the daily check). An age key pair:
   `age-keygen -o tropicast-backup.key`. Keep the private key **offline**
   (password manager, operator machine). It is never stored on the node or in
   GitHub: only the public key (`age1…`) is.
4. **Cloudflare**: an API token with *Zone → DNS → Edit* on
   `tropicastradio.com`, and the zone ID (zone *Overview*).
5. **SSH keys** (ed25519, no passphrase, generated on your machine):
   - `deploy`: the workflow logs in to the node as `deploy`.
   - `provisioning`: the API pushes station limits to tc-stream-1. Add its
     public key to tc-streaming's `provisioning_ssh_keys` and run that
     playbook (it allows only `deploy.sh apply-stations`).
6. **Source-auth password**: `openssl rand -base64 32`. The same value goes to
   tc-streaming's `ICECAST_SOURCE_AUTH_PASSWORD` at the cutover.

| Name | Kind | Value |
|---|---|---|
| `HCLOUD_TOKEN` | secret | Hetzner Cloud API token (read/write), project of tc-stream-1 |
| `TF_STATE_ACCESS_KEY`, `TF_STATE_SECRET_KEY` | secret | Object Storage key of the Terraform state bucket (same as tc-streaming) |
| `CLOUDFLARE_API_TOKEN` | secret | step 4 |
| `TF_STATE_BUCKET`, `TF_STATE_LOCATION` | variable | `tropicast-tfstate`, `fsn1` |
| `TFVARS` | variable | content of `infra/terraform/terraform.tfvars` (see `.example`) |
| `DEPLOY_SSH_KEY` | secret | `deploy` private key |
| `DEPLOY_HOST` | variable | `terraform output ipv4` |
| `DEPLOY_KNOWN_HOSTS` | variable | `ssh-keyscan -t ed25519 <ipv4>`, checked against the Hetzner console |
| `DATABASE_POOLED_URL`, `DATABASE_DIRECT_URL` | secret | step 1 |
| `SMTP_PASSWORD` | secret | step 2 |
| `SMTP_HOST`, `SMTP_USERNAME`, `EMAIL_FROM` | variable | step 2; e.g. `Tropicast <no-reply@tropicastradio.com>` |
| `SOURCE_AUTH_PASSWORD` | secret | step 6 |
| `PROVISIONING_SSH_KEY` | secret | `provisioning` private key |
| `PROVISIONING_SSH_HOST` | variable | `10.20.1.2` |
| `PROVISIONING_SSH_HOST_KEY` | variable | section 4 |
| `PROVISIONING_ENABLED` | variable | `false` until the cutover, then `true` |
| `BACKUP_S3_ACCESS_KEY`, `BACKUP_S3_SECRET_KEY` | secret | step 3 |
| `BACKUP_S3_READ_ACCESS_KEY`, `BACKUP_S3_READ_SECRET_KEY` | secret | a **read-only** key on the backup bucket, for the daily backup check |
| `BACKUP_S3_ENDPOINT`, `BACKUP_S3_BUCKET`, `BACKUP_AGE_RECIPIENT` | variable | `https://fsn1.your-objectstorage.com`, bucket, `age1…` |
| `COMPOSE_PROFILES` | variable | `monitoring` to run Alloy and export metrics and traces (section 9) |
| `GRAFANA_CLOUD_OTLP_URL`, `GRAFANA_CLOUD_OTLP_USER` | variable | Grafana Cloud stack → *OpenTelemetry*: the OTLP endpoint and instance ID |
| `GRAFANA_CLOUD_TOKEN` | secret | Grafana Cloud access policy token with `metrics:write` and `traces:write` |
| `STATS_URL` | variable | `http://10.20.1.2:9100/metrics` once tc-streaming sets `PRIVATE_IP` (section 4); empty: no live status |
| `DIRECTORY_ENABLED` | variable | `true` to add opted-in stations to RadioBrowser (section 4, step 5); default `false` |
| `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` | secret | Stripe secret key and the webhook signing secret (section 10); empty: billing off |
| `STRIPE_PRICE_STARTER`, `STRIPE_PRICE_GROWTH`, `STRIPE_PRICE_PRO` | variable | Stripe price IDs (`price_…`) of the monthly prices |
| `PRICE_LABEL_STARTER`, `PRICE_LABEL_GROWTH`, `PRICE_LABEL_PRO` | variable | Prices shown in the app, e.g. `€9 / month` |
| `ACME_EMAIL`, `OPERATOR_EMAIL` | variable | Let's Encrypt contact; your account email (operator pages) |

Optional variables: `APP_HOST` (default `app.tropicastradio.com`),
`DEPLOY_SERVER` (`tc-app-1`), `PRIVATE_IP` (`10.20.1.10`), `SMTP_PORT`
(`587`), `SOURCE_AUTH_USER` (`icecast`), `BACKUP_PG_IMAGE`
(`postgres:17-alpine`: match Neon's major version).

Values must not contain a single quote (`'`): the workflow writes them quoted.

## 2. Provision the node

1. **Terraform**: run the *Terraform deploy* workflow with `action=plan`.
   The state has no lock file (Hetzner Object Storage does not enforce
   conditional writes); the workflow runs one at a time instead, so apply
   only through it. Read the plan in the run summary, then run it with `action=apply` and that
   run's ID. It creates `tc-app-1`, its firewall and primary IPs, the private
   network, attaches **tc-stream-1 at 10.20.1.2** (live, no restart), and the
   `app` A/AAAA records (DNS only).
2. **Check the private network on tc-stream-1** (Hetzner's Debian image
   configures it automatically):

   ```sh
   ssh ops@<tc-stream-1> ip -4 addr | grep 10.20.1.2
   ```

3. **Ansible** (from your machine):

   ```sh
   cd infra/ansible
   cp inventory.example.ini inventory.ini      # IPv4 from terraform output, ansible_user=root
   mkdir -p group_vars                         # ops_ssh_keys, deploy_ssh_keys: group_vars_example.yml
   ansible-playbook site.yml
   ```

   Then set `ansible_user=ops` in `inventory.ini`: root login is now off.
   Later runs apply only changes.
4. Set `DEPLOY_HOST` and `DEPLOY_KNOWN_HOSTS` (table above).

## 3. Deploy

Every commit on main builds `ghcr.io/tropicast/dashboard:sha-<commit>` and the
migration image (*Image* workflow). To deploy, run the **Deploy** workflow
with `ref=main` (or a SHA on main). It:

1. writes `/opt/tc-dashboard/.env` from the secrets and variables;
2. uploads the bundle (compose file, Caddyfile, scripts, image tag);
3. runs `deploy.sh activate`: pull, **migrate** (direct connection), start the
   new API, wait for `/health/ready` (the API answers and reaches the database);
4. if the new API is not healthy, starts the release that was running before
   and fails;
5. checks `https://app.tropicastradio.com/health/ready`, the SPA, the redirect to
   HTTPS, and that `/internal/*` is not public.

The first deploy waits up to 2 minutes for the Let's Encrypt certificate.

On the node (`ssh ops@<ipv4>`, then `sudo -iu deploy`):

```sh
/opt/tc-dashboard/deploy.sh status                  # current and previous release, containers
/opt/tc-dashboard/deploy.sh compose logs -f api     # logs
```

### Migrations and rollback

Migrations only **add** (expand); a later release removes what is unused
(contract). So the previous image always works with the migrated database.

- **Roll back** to the previous release: on the node,
  `/opt/tc-dashboard/deploy.sh rollback` (no pull, no migration), or run
  *Deploy* with the older SHA.
- A migration is never rolled back automatically. If one must be undone,
  restore the database (section 5) or ship a new migration.

## 4. Cutover from the stand-ins (with tc-streaming#42)

Run in this order; each step can be undone by reverting its variable.

1. **Source auth.** On tc-streaming set
   `ICECAST_SOURCE_AUTH_URL=http://10.20.1.10:8081/internal/icecast/source-auth`
   and `ICECAST_SOURCE_AUTH_PASSWORD` to this repository's
   `SOURCE_AUTH_PASSWORD`. Remove `stub` from `COMPOSE_PROFILES`, then deploy
   tc-streaming. Check from tc-stream-1:
   `curl -s -o /dev/null -w '%{http_code}\n' http://10.20.1.10:8081/health`
   gives `200`, and a test broadcast with a dashboard credential goes live.
2. **Provisioning.** Record tc-stream-1's host key, as seen over the private
   network:

   ```sh
   ssh ops@<tc-app-1> 'ssh-keyscan -t ed25519 10.20.1.2 2>/dev/null | ssh-keygen -lf -'
   ```

   Compare it with `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` on
   tc-stream-1. Set `PROVISIONING_SSH_HOST_KEY` to the `SHA256:…` value,
   `PROVISIONING_ENABLED=true`, and deploy. Within a minute the API pushes
   `stations.json` (logs: "Applied station limits").
3. Remove `STATION_LIMITS` from tc-streaming's variables.
4. **Live status and statistics** (#10). On tc-streaming set
   `PRIVATE_IP=10.20.1.2` (its exporter then also listens there) and deploy
   it. Check from tc-app-1:
   `curl -s http://10.20.1.2:9100/metrics | grep -c icecast_mount_listeners`.
   Then set `STATS_URL=http://10.20.1.2:9100/metrics` here and deploy. A
   station shows live within 15 s of Go Live; *Stats of node … unavailable*
   in the API logs means the exporter cannot be reached.

5. **RadioBrowser listing** (#13), after tc-streaming's directory headers
   (tc-streaming#45) are deployed: set `DIRECTORY_ENABLED=true` and deploy.
   - An opted-in station (Starter plan and above, with a country) is added
     once, the first time it is live. RadioBrowser's API cannot edit or delete
     entries, so later changes reach the listing through the stream: the node
     sends the station's metadata as `icy-*` headers with
     `icy-index-metadata: 1`, which RadioBrowser applies at its next check.
   - Opting out sends `icy-do-not-index: 1`; at its next check RadioBrowser
     marks the entry broken, which hides it from most apps.
   - Check: the station page shows *Listed on RadioBrowser* with a link, and
     `curl -sI https://listen.tropicastradio.com/stations/<id>/live.mp3 | grep -i icy-`
     shows the headers. An outage only delays a listing (retried with
     backoff up to every 6 hours); nothing else waits for it.

## 5. Backups and restore

`tc-dashboard-backup.timer` runs `backup.sh` every night at 02:30 UTC: a
`pg_dump` of the direct connection, encrypted with age, uploaded to
`s3://<bucket>/db/tropicast-<time>.dump.age`. Dumps older than 30 days are
deleted.

```sh
systemctl list-timers tc-dashboard-backup.timer
journalctl -u tc-dashboard-backup --since yesterday
sudo systemctl start tc-dashboard-backup      # back up now
```

**Daily check**: the *Backup check* workflow (06:17 UTC) fails when the
newest dump is older than 26 hours or nearly empty. It lists the bucket with
a read-only key and cannot decrypt anything.

**Monthly restore test**: on the 1st, *Backup check* opens a "Restore test"
issue. On an operator machine with Docker, `age` and the private key, restore
the newest dump into a throwaway PostgreSQL, then close the issue with the
output:

```sh
export BACKUP_S3_ENDPOINT=… BACKUP_S3_BUCKET=… BACKUP_S3_ACCESS_KEY=… BACKUP_S3_SECRET_KEY=…   # read-only key is enough
BACKUP_AGE_KEY_FILE=tropicast-backup.key deploy/restore-test.sh
```

It checks the migrations and the main tables. The private key never leaves
that machine.

**Restore into a fresh database** (e.g. after data loss):

1. Create the target: a new Neon branch or database (empty), PostgreSQL 17.
2. On your machine, with Docker, `age` and the age private key: check the
   newest dump, then restore it into the new database.

   ```sh
   export BACKUP_S3_ENDPOINT=… BACKUP_S3_BUCKET=… BACKUP_S3_ACCESS_KEY=… BACKUP_S3_SECRET_KEY=…
   BACKUP_AGE_KEY_FILE=tropicast-backup.key RESTORE_TARGET_URL='<direct URI of the new database>' \
     deploy/restore-test.sh                     # newest dump; BACKUP_NAME=tropicast-….dump.age for another
   ```

3. Point `DATABASE_POOLED_URL` and `DATABASE_DIRECT_URL` at the new database,
   then run *Deploy*. Migrations newer than the dump are applied.
4. Changes after the dump are lost. Sessions keep working; devices may need to
   sign in again if their refresh token rotated after the dump.

## 6. Rotate secrets

Update the secret, then run *Deploy* (it rewrites `.env` and recreates the
API) unless noted.

| Secret | How |
|---|---|
| Database password | Reset the role password in Neon, update both URIs, deploy. |
| `SMTP_PASSWORD` | New SMTP key at the provider, update, deploy, delete the old key. |
| `SOURCE_AUTH_PASSWORD` | Brief source-auth outage: update here and tc-streaming's `ICECAST_SOURCE_AUTH_PASSWORD`, deploy both (dashboard first). Live sources stay connected; only new connects are refused in between. |
| `PROVISIONING_SSH_KEY` | Add the new public key to tc-streaming `provisioning_ssh_keys`, run its playbook, update the secret, deploy, then remove the old key there. |
| `DEPLOY_SSH_KEY` | Add the new key to `deploy_ssh_keys`, run `site.yml`, update the secret, remove the old key and run again. |
| Backup keys | New Object Storage keys: update them, deploy. New age key pair: update `BACKUP_AGE_RECIPIENT`, deploy; keep the old private key offline while old dumps exist (30 days). |
| `STRIPE_SECRET_KEY` | Roll the key in Stripe (*Developers → API keys*), update the secret, deploy. |
| `STRIPE_WEBHOOK_SECRET` | Roll the signing secret of the endpoint in Stripe, update the secret, deploy within Stripe's overlap window. |
| `CLOUDFLARE_API_TOKEN`, `HCLOUD_TOKEN` | Roll the token at the provider and update the secret (no deploy). |
| Data Protection keys | They live in the database (`data_protection_keys`) and roll over every 90 days by themselves. To revoke them all at once (suspected leak of a database copy): `DELETE FROM data_protection_keys;` then restart the API (`deploy.sh compose restart api`). Signs **everyone** out (browser sessions and desktop devices). |

## 7. Rebuild the node from scratch

The node holds no data that is not elsewhere: Data Protection keys are in the
database, and Caddy's certificates are re-issued.

1. Turn off delete and rebuild protection on `tc-app-1` in the Hetzner
   console, delete the server (the primary IPs stay), and run *Terraform
   deploy* (plan, then apply).
2. Remove the old host key from `DEPLOY_KNOWN_HOSTS` and set the new one.
3. Run `infra/ansible/site.yml` as `root`, then switch to `ops`.
4. Run *Deploy*. Everything else (database, backups, DNS) is unchanged.

## 8. Troubleshooting

- **API logs**: JSON lines, credentials masked (`deploy.sh compose logs api`).
  Account security events (sign-ins, failed sign-ins, devices, password
  changes) are in `security_events` and shown to each user; tenant actions in
  the audit log (`GET /api/v1/tenants/current/audit`).

- **Deploy fails at "Open SSH"**: `HCLOUD_TOKEN` lacks write access, or a
  leftover CI firewall exists (the next run removes it).
- **Health check fails after "migrating the database"**: read
  `deploy.sh compose logs api`. The previous release is running again.
- **Emails do not arrive**: check SPF/DKIM at the provider and
  `deploy.sh compose logs api | grep -i smtp`.
- **Icecast refuses every source after the cutover**: from tc-stream-1,
  `curl http://10.20.1.10:8081/health`. If it fails, check the private network
  (section 2) and that the API publishes `10.20.1.10:8081`
  (`deploy.sh compose ps`).

## 9. Monitoring and alerts

With `COMPOSE_PROFILES=monitoring`, Alloy on the node receives the API's
OpenTelemetry metrics and traces (OTLP, internal network) and adds host metrics
and the backup age. It sends all of them to Grafana Cloud's OTLP gateway: the
same stack as tc-streaming, with an access policy token that can write metrics
and traces.

1. Set `COMPOSE_PROFILES=monitoring`, `GRAFANA_CLOUD_OTLP_URL`,
   `GRAFANA_CLOUD_OTLP_USER` and the `GRAFANA_CLOUD_TOKEN` secret, then deploy.
   In Grafana *Explore*, `tropicast_up{job="tc-dashboard"}` shows 1 within a
   minute.
2. Load the alert rules (`monitoring/rules.yaml`, unit-tested in CI):

   ```sh
   mimirtool rules load monitoring/rules.yaml --address=<Prometheus URL without /api/prom> --id=<instance ID> --key=<token with rules:write>
   ```

3. Route the alerts in Grafana *Alerting → Contact points* (email).

| Alert | Fires when |
|---|---|
| DashboardDown | no API metrics for 3 minutes |
| SourceAuthErrors | any source-auth call failed closed in the last 5 minutes (alert within ~2 minutes) |
| SourceAuthSlow | source-auth p99 above 500 ms for 5 minutes |
| ProvisioningFailing | station limits failing to apply for 20 minutes |
| StreamingStatsUnavailable | the streaming node's exporter unreadable for 10 minutes |
| DirectoryListingFailing | more than 3 failed RadioBrowser attempts in 6 hours (info) |
| BackupMissing, BackupNeverRecorded | no successful backup for 26 hours |

| BillingWebhookErrors | a Stripe webhook failed to apply (Stripe retries) |
| BillingWebhookSignatures | more than 10 webhook calls with a bad signature in an hour |

## 10. Billing (Stripe)

Design: [ADR 0002](adr/0002-payments-stripe.md). Do it in **test mode** first
(`sk_test_…`), then repeat in live mode.

1. **Products and prices**: in Stripe *Product catalog*, one product per paid
   plan (Starter, Growth, Pro) with a **recurring monthly** price. Set the
   price IDs in `STRIPE_PRICE_*` and the labels in `PRICE_LABEL_*`.
2. **Customer portal** (*Settings → Billing → Customer portal*):
   - allow updating payment methods, viewing invoices and canceling (**at the
     end of the billing period**);
   - allow switching between the three prices, with **downgrades applied at
     period end**.
3. **Webhook endpoint** (*Developers → Webhooks*):
   - URL `https://app.tropicastradio.com/api/v1/billing/webhook`;
   - events: `checkout.session.completed`, `customer.subscription.created`,
     `customer.subscription.updated`, `customer.subscription.deleted`;
   - copy its signing secret (`whsec_…`) into `STRIPE_WEBHOOK_SECRET`, and the
     secret key into `STRIPE_SECRET_KEY`. Deploy.
4. **Check** (test mode):
   - as a workspace Owner, *Account → Plan and billing → Start free trial* on
     Starter;
   - pay with card `4242 4242 4242 4242`;
   - back in the app the plan shows Starter, and within a minute the node's
     `stations.json` has `max_listeners: 500` for the workspace's stations
     (`GET /api/v1/operator/streaming-nodes` shows `inSync: true`);
   - in Stripe, *Webhooks → the endpoint* shows deliveries with `200`;
     resending one changes nothing (`Billing webhook … Duplicate` in the API
     logs).
5. **Failed payments**: Stripe's *Smart retries* and emails handle dunning.
   The app keeps the paid plan for 7 days after the subscription becomes past
   due, then moves the workspace to Free. Paying later restores the plan.

**Fair use**: `GET /api/v1/operator/usage?month=2026-10` lists every tenant's
peak listeners, listener-hours and estimated egress, heaviest first.

