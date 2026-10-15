# Media library (AutoDJ uploads)

Stations on a plan with AutoDJ (#103) upload tracks to a private S3-compatible
bucket (#104). Production uses Hetzner Object Storage
([runbook](../runbook.md#11-autodj-storage-uploaded-tracks)); `compose.yaml`
runs RustFS on `localhost:9000` with the bucket `tracks`.

## Upload

1. `POST /api/v1/stations/{id}/tracks/uploads` with `fileName` and `size`.
   The extension must be `.mp3`, `.flac`, `.ogg`, `.oga` or `.opus`, the size
   at most 200 MB, and the declared size must fit the quota. The answer has a
   presigned `uploadUrl` (30 minutes), the `Content-Type` header to send, and
   the `trackId`. A pending track row holds the declared size in the quota.
2. The browser PUTs the file to `uploadUrl` with that header. The URL is signed
   for it: another content type is refused by the bucket.
3. `POST /api/v1/stations/{id}/tracks` with `uploadId` (and optional `title`,
   `artist`). The server reads the stored object, not the client's claims:
   its size (HEAD), then its first 256 KB and last 64 KB (`AudioProbe`) for the
   format, duration, sample rate and tags. MP3 (CBR or Xing/Info/VBRI VBR),
   FLAC, and Ogg Opus or Vorbis are accepted, up to 3 hours. A file that is
   not readable audio (422), too large, or over the quota at its real size
   (403) is deleted from the bucket at once, with its row. `409` means the file
   has not arrived yet; confirm again.

Title and artist come from the request, else the file's tags (ID3v2, ID3v1,
Vorbis comments), else the file name.

## Quota

`Plan.StorageQuotaBytes` is per workspace, across its stations: Free 0 (no
AutoDJ), Starter 1 GB, Growth 5 GB, Pro 20 GB. Confirmed tracks and pending
uploads count. The tenant (`storageUsedBytes`), the billing usage (`storage`)
and the library (`storage`) report it. The workspace row is locked while
checking, so concurrent uploads cannot pass the quota together.

## Deletion

| What                    | How                                                                                           |
| ----------------------- | --------------------------------------------------------------------------------------------- |
| One track               | `DELETE /api/v1/stations/{id}/tracks/{trackId}`: object, then row                             |
| A station               | `StationDeleted` → `StationTracksCleanup`: every object under `stations/{id}/`, then the rows |
| An unconfirmed upload   | `PendingUploadCleanup`, every 10 minutes after the 30-minute expiry                           |
| An account or workspace | Needs its stations deleted first, so the station cleanup covers it                            |

Object keys are `stations/{station id}/tracks/{track id}` (no user data in the
key). Music rights are the station's responsibility (Terms of Service).
