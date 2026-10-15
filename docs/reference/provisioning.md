# Provisioning (station limits on the streaming node)

The database is the desired state of the streaming node. A background worker
renders it as tc-streaming's `stations.json` (one entry per station: plan,
listener cap, bitrate, formats, and `"autodj": true` while AutoDJ is on and in
the plan, so the node adds the station's hidden `autodj.<fmt>` fallback
mounts). When its version differs from the one last
applied, the worker sends it over SSH to the node, where `deploy.sh
apply-stations` reloads Icecast with no restart.

- **When it runs:** on station create or delete, plan changes and AutoDJ on or
  off (`AutoDjChanged`), through the outbox within seconds, and at least every
  30 s (`Provisioning:Interval`).
- **Failures:** retried with exponential backoff from 15 s, up to 10 min.
  They never block the dashboard. The operator view
  (`GET /api/v1/operator/streaming-nodes`) shows desired and applied versions,
  failures and the next try. `POST …/{node}/reconcile` retries at once.
  Operators are the accounts listed in `Operators:Emails`.
- **One applier:** a PostgreSQL advisory lock keeps it to one API instance at
  a time.
- **Capacity guard:** creating a station returns 503, and logs a critical
  "node is full" error, when the node would exceed Icecast's `<sources>`
  (`Streaming:MaxSources`, 50; every allowed format of a station counts, and
  again for a station with AutoDJ on) or the oversubscribed sum of listener
  caps (`Streaming:MaxListenerCaps`, 15,000). Turning AutoDJ on is refused the
  same way (503) when its mounts would not fit.

Production settings (`Provisioning:Ssh`):

| Setting         | Value                                                                                                                                                                                   |
| --------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Host`, `Port`  | The streaming node over the private network, `22`                                                                                                                                       |
| `Username`      | `deploy`                                                                                                                                                                                |
| `PrivateKey`    | An ed25519 key used only for this (secret). On the node, its public key is installed with the forced command `deploy.sh apply-stations` (tc-streaming Ansible `provisioning_ssh_keys`). |
| `HostKeySha256` | The node's ed25519 host key: `ssh-keyscan -t ed25519 <host> \| ssh-keygen -lf -`                                                                                                        |

Compose sets `Provisioning__Enabled=false`: locally there is no node.

## Playout worker endpoint

`GET /internal/playout/stations` tells the Liquidsoap playout worker
(tc-streaming#61) what to play (#107). Like source auth it is served on the
internal port 8081 only (404 on the public port), with the node's HTTP Basic
credentials (401 otherwise), and is never cached (`Cache-Control: no-store`).

```json
{
    "version": "3f9c0a1b2d4e5f60",
    "urlsExpireAt": "2026-10-15T18:15:00+00:00",
    "stations": [
        {
            "publicId": "k3m9x2p7qa",
            "username": "k3m9x2p7qa",
            "password": "<the station's AutoDJ credential>",
            "outputs": [
                {
                    "format": "mp3",
                    "mount": "/stations/k3m9x2p7qa/autodj.mp3",
                    "contentType": "audio/mpeg",
                    "bitrateKbps": 128,
                    "sampleRate": 44100,
                    "channels": 2
                }
            ],
            "playlist": {
                "id": "…",
                "name": "Fallback",
                "shuffle": false,
                "tracks": [
                    {
                        "id": "…",
                        "title": "Jingle",
                        "artist": "",
                        "durationMs": 30000,
                        "format": "Mp3",
                        "url": "https://…"
                    }
                ]
            }
        }
    ]
}
```

- **Which stations:** on this node (`Streaming:Node`), not deleted, tenant
  active, plan with AutoDJ, AutoDJ on, within the plan's station count (as
  source auth), and at least one confirmed track in the default playlist.
- **Outputs:** one per format of the plan, with the encoding pinned from the
  live source ([AutoDJ](autodj.md#encoding-and-credential)).
- **Credential:** the station's AutoDJ source credential, accepted on its
  `autodj.<fmt>` mounts only. One that cannot be decrypted (lost Data
  Protection keys) is rotated on the spot.
- **Track URLs:** presigned GETs from the private bucket, valid
  `Storage:DownloadUrlLifetime` (15 minutes); download them to a local cache.
- **Polling:** `version` (also the `ETag`) changes when a station, its
  encoding, credential or playlist changes, not when URLs are renewed. Poll
  with `If-None-Match` (304 while unchanged), and fetch without it before
  `urlsExpireAt`.
