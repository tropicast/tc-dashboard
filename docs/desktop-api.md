# Desktop API (contract version 1)

The API the desktop app ([tc-station](https://github.com/tropicast/tc-station))
uses to sign in, pick a station and go live, without the user typing a host,
mount or password. The full schema is in the OpenAPI document
(`/openapi/v1.json`, tag *Desktop* and the `/auth/device` paths of tag *Auth*).

## Versioning

- Version 1 is everything under `/api/v1` on this page.
- Within v1, fields may be **added** to responses and new values may appear in
  `outputs` (a new format). The app ignores what it does not know.
- Removing or renaming a field, or changing its meaning, makes a v2 under
  `/api/v2`. v1 stays available until every supported app release uses v2.

## 1. Sign in: device authorization

OAuth 2.0 device authorization style ([RFC 8628](https://www.rfc-editor.org/rfc/rfc8628)),
with JSON bodies. The app never sees the user's password. See
[ADR 0001](adr/0001-desktop-sign-in.md).

```text
App                                API                         User's browser
 | POST /auth/device/code ------->  |                               |
 | <- deviceCode, userCode,         |                               |
 |    verificationUri, interval     |                               |
 | shows userCode + opens  ---------------------------------------> | /device?code=BCDF-GHJK
 |                                  | <-- GET  /auth/device/{code}  | (signed in: shows the device name)
 |                                  | <-- POST /auth/device/approve |
 | POST /auth/device/token (poll) ->|                               |
 | <- accessToken, refreshToken     |                               |
```

**Start:** `POST /api/v1/auth/device/code`

```json
{ "deviceName": "Studio PC" }
```

```json
{
  "deviceCode": "0192…9f.Xr4…",
  "userCode": "BCDF-GHJK",
  "verificationUri": "https://app.tropicastradio.com/device",
  "verificationUriComplete": "https://app.tropicastradio.com/device?code=BCDF-GHJK",
  "expiresIn": 600,
  "interval": 5
}
```

- `deviceName` (1–64 characters) becomes the device's name in the account and
  the label of its broadcast credentials.
- `deviceCode` is a secret: keep it in memory, send it only to `/token`.
- Show `userCode`. Open `verificationUriComplete` in the browser, or show it.
- The per-IP sign-in rate limit applies (10 a minute).

**Poll:** `POST /api/v1/auth/device/token` every `interval` seconds.

```json
{ "deviceCode": "0192…9f.Xr4…" }
```

`200` returns the same tokens as `/auth/token`: `tokenType`, `accessToken`
(15 minutes), `expiresIn`, `refreshToken` (one-time, 60 days sliding). The
response is `Cache-Control: no-store`. A device code gives tokens **once**.

`400` is a Problem Details body with an `error` member:

| `error` | Meaning | App does |
|---|---|---|
| `authorization_pending` | Not approved yet | Poll again after `interval` |
| `slow_down` | Polled too soon | Wait longer, then poll |
| `access_denied` | The user refused | Stop; offer to start again |
| `expired_token` | Expired (10 minutes), already used, or unknown | Stop; start again |

**Approve** (web page, signed-in user; for the SPA, #11):

- `GET /api/v1/auth/device/{userCode}` shows `deviceName`, `createdAt` and
  `expiresAt`, so the user checks it is their device.
- `POST /api/v1/auth/device/approve` or `/deny` with `{ "userCode": "BCDF-GHJK" }`.
- User codes accept any case, with or without the dash. Unknown, expired or
  already decided codes give `404`.

**Then**, as with `/auth/token`:

- `POST /api/v1/auth/token/refresh` with `{ "refreshToken": … }` before the
  access token expires; store the new refresh token. Reusing an old one signs
  the device out.
- `POST /api/v1/auth/token/revoke` signs the device out.

Store the refresh token in the OS secret store, never in a plain file.

## 2. Pick a station

`GET /api/v1/me/stations` with `Authorization: Bearer <accessToken>`.

```json
[
  {
    "tenantId": "0192…",
    "tenantName": "My radio group",
    "stationId": "0192…",
    "publicId": "k3m9x2p7qa",
    "name": "Radio Mada",
    "role": "Owner"
  }
]
```

Every station of every tenant the user belongs to, in any role (Owner, Admin
and Broadcaster can all broadcast). Stations of suspended tenants and deleted
stations are left out.

## 3. Go live: the broadcast target

`POST /api/v1/stations/{stationId}/broadcast-target` with the bearer token and
`X-Tenant-Id: <tenantId>` from step 2. No body.

```json
{
  "stationId": "0192…",
  "publicId": "k3m9x2p7qa",
  "stationName": "Radio Mada",
  "credentialId": "0192…",
  "username": "k3m9x2p7qa",
  "password": "kq3…43 characters…",
  "maxBitrateKbps": 128,
  "outputs": [
    {
      "format": "Mp3",
      "contentType": "audio/mpeg",
      "ingestUrl": "https://ingest.tropicastradio.com/stations/k3m9x2p7qa/live.mp3",
      "listenerUrl": "https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.mp3"
    },
    {
      "format": "Opus",
      "contentType": "audio/ogg",
      "ingestUrl": "https://ingest.tropicastradio.com/stations/k3m9x2p7qa/live.opus",
      "listenerUrl": "https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.opus"
    }
  ]
}
```

- Publish each output with an HTTP `PUT` to its `ingestUrl` (Icecast source
  protocol over TLS, port 443), Basic auth `username:password`, the output's
  `contentType`, and `Ice-Bitrate` at most `maxBitrateKbps`. A higher declared
  bitrate, or a format the plan lacks, is refused at connect.
- `outputs` lists the formats the plan allows, MP3 first.
- `password` belongs to this device and station. It is shown only in this
  response (`Cache-Control: no-store`): store it in the OS secret store.
  **Each call replaces it**: the previous password of this device for this
  station stops working at its next connect. Ask again when the stored one is
  lost or refused.
- The station's admins see the credential under the station's credentials,
  labelled with the device name, and can revoke it.
- Signing the device out (`/auth/token/revoke`, *Devices* in the web app, or a
  password change or reset) revokes every password the device got this way.

Errors:

| Status | When |
|---|---|
| `401` | No or expired access token: refresh it |
| `403` | Not a member of the tenant; the token is a browser session, not a device; the device was signed out; or the tenant is suspended |
| `404` | No such station in this tenant (or deleted) |
| `409` | Two calls for the same station at the same moment: try again |

The response never contains node, admin or relay credentials, nor the
Icecast node's name.
