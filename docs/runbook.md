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
   and an access key limited to it. An age key pair:
   `age-keygen -o tropicast-backup.key`. Keep the private key **offline**
   (password manager); only the public key (`age1…`) goes to the node.
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
| `BACKUP_AGE_KEY` | secret | the age **private** key, for the monthly restore test only |
| `BACKUP_S3_ENDPOINT`, `BACKUP_S3_BUCKET`, `BACKUP_AGE_RECIPIENT` | variable | `https://fsn1.your-objectstorage.com`, bucket, `age1…` |
| `ACME_EMAIL`, `OPERATOR_EMAIL` | variable | Let's Encrypt contact; your account email (operator pages) |

Optional variables: `APP_HOST` (default `app.tropicastradio.com`),
`DEPLOY_SERVER` (`tc-app-1`), `PRIVATE_IP` (`10.20.1.10`), `SMTP_PORT`
(`587`), `SOURCE_AUTH_USER` (`icecast`), `BACKUP_PG_IMAGE`
(`postgres:17-alpine`: match Neon's major version).

Values must not contain a single quote (`'`): the workflow writes them quoted.

## 2. Provision the node

1. **Terraform**: run the *Terraform deploy* workflow with `action=plan`, read
   the plan in the run summary, then run it with `action=apply` and that
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
   new API, wait for `/health`;
4. if the new API is not healthy, starts the release that was running before
   and fails;
5. checks `https://app.tropicastradio.com/health`, the SPA, the redirect to
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

**Monthly restore test**: the *Restore test* workflow (1st of the month, or
run it by hand) restores the newest dump into a throwaway PostgreSQL and
checks the migrations and tables. A failed run sends the usual GitHub
notification.

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
| Backup keys | New Object Storage key: update both, deploy. New age key pair: update `BACKUP_AGE_RECIPIENT` and `BACKUP_AGE_KEY`; keep the old private key while old dumps exist (30 days). |
| `CLOUDFLARE_API_TOKEN`, `HCLOUD_TOKEN` | Roll the token at the provider and update the secret (no deploy). |
| Data Protection keys | On the node: `deploy.sh compose down api && docker volume rm tc-dashboard_dataprotection`, then deploy. Signs **everyone** out (browser sessions and desktop devices). |

## 7. Rebuild the node from scratch

The node holds no data that is not elsewhere, except the Data Protection keys
(losing them signs everyone out) and Caddy's certificates (re-issued).

1. Turn off delete and rebuild protection on `tc-app-1` in the Hetzner
   console, delete the server (the primary IPs stay), and run *Terraform
   deploy* (plan, then apply).
2. Remove the old host key from `DEPLOY_KNOWN_HOSTS` and set the new one.
3. Run `infra/ansible/site.yml` as `root`, then switch to `ops`.
4. Run *Deploy*. Everything else (database, backups, DNS) is unchanged.

## 8. Troubleshooting

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
