# REST API

All under `/api/v1`. Errors are RFC 9457 Problem Details, with field errors for
validation (FluentValidation in Application). In Development, the OpenAPI
document is at `/openapi/v1.json`, and an interactive reference with examples
is at `/scalar`.

| Endpoint                                           | Policy    | Notes                                                                                               |
| -------------------------------------------------- | --------- | --------------------------------------------------------------------------------------------------- |
| `POST /tenants`                                    | signed in | Creates a tenant on the Free plan; the caller is Owner                                              |
| `GET /tenants/current`                             | Member    | Plan, limits and station count; `ETag`                                                              |
| `PATCH /tenants/current`                           | Admin     | Needs `If-Match`                                                                                    |
| `GET /tenants/current/members`                     | Member    |                                                                                                     |
| `DELETE /tenants/current/members/{userId}`         | Admin     | Only an Owner removes an Owner; the last Owner stays                                                |
| `POST /tenants/current/invitations`                | Admin     | How members are added                                                                               |
| `GET /stations?page=&pageSize=`                    | Member    | Paged (1-100 per page)                                                                              |
| `POST /stations`                                   | Admin     | Within the plan's station limit; assigns the stream and returns listener URLs                       |
| `GET /stations/{id}`                               | Member    | `ETag`                                                                                              |
| `PATCH /stations/{id}`                             | Admin     | Needs `If-Match` (428 without, 412 when stale)                                                      |
| `DELETE /stations/{id}`                            | Admin     | Soft delete; optional `If-Match`                                                                    |
| `GET /stations/{id}/credentials`                   | Admin     | Per-device broadcast credentials, active first; never the secret                                    |
| `POST /stations/{id}/credentials`                  | Admin     | Issues a 256-bit secret for one device; the only response that shows it (`Cache-Control: no-store`) |
| `DELETE /stations/{id}/credentials/{credentialId}` | Admin     | Revokes that device only; idempotent                                                                |

Stations of other tenants answer 404. Broadcast credentials are stored as
SHA-256 hashes; the Icecast username is the station's public ID. Credential
creation and revocation are recorded in `audit_entries` (actor, action,
target; never secrets). Listener URLs come from `Streaming`
settings (`Node`, `ListenerBaseUrl`). A station change is saved together with
an `outbox_messages` row (`StationCreated`, `StationChanged`,
`StationDeleted`). A background dispatcher hands these rows to the
`IOutboxConsumer` implementations (provisioning #8, RadioBrowser #13), at least
once, retrying failures with backoff (`Outbox` settings).
