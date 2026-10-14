# Source auth for Icecast

`POST /internal/icecast/source-auth` implements tc-streaming's
[source-auth contract](https://github.com/tropicast/tc-streaming/blob/main/docs/source-auth.md):
Icecast asks it before accepting audio from a source.

- **Internal port only.** It is served on port 8081 (`SourceAuth:Port`) and
  answers 404 on the public port 8080, so the public gateway never routes it.
  Only the streaming node, on the private network, reaches it.
- **Node credentials.** Icecast authenticates with HTTP Basic
  (`SourceAuth:NodeUsername` / `NodePassword`, the node's
  `ICECAST_SOURCE_AUTH_USER` / `_PASSWORD`), compared in constant time. When
  they are not set, every call gets 401.
- **Allow** (`200` with `icecast-auth-user: 1`) only when all of these hold:
  the mount is `/stations/{public id}/live.(mp3|opus)`, the user is that
  station, the password matches one of its active (unrevoked) credentials,
  the tenant is active, the plan allows the format, and a declared bitrate
  (`Ice-Bitrate`, else `Ice-Audio-Info`) is within the plan.
- **Deny** returns `200` with `icecast-auth-message: <reason>`. A reason never
  contains the credential. A database timeout (2 s) or any error also denies.
- **On allow,** it records the credential's `last_used_at` and opens a
  `LiveSession`.

To try it with a real Icecast, run tc-streaming's `icecast` service with
`ICECAST_SOURCE_AUTH_URL=http://api:8081/internal/icecast/source-auth` and
matching node credentials, and attach it to this stack's network
(`docker network connect tc-dashboard_default tc-streaming-icecast-1`).
