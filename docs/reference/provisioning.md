# Provisioning (station limits on the streaming node)

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
