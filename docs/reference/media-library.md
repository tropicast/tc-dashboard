# Media library (AutoDJ uploads)

Stations on a plan with AutoDJ (#103) upload tracks to a private S3-compatible
bucket (#104). Production uses Hetzner Object Storage
([runbook](../runbook.md#11-autodj-storage-uploaded-tracks)); `compose.yaml`
runs RustFS on `localhost:9000` with the bucket `tracks`.

## In the dashboard

The station page links to its **Media library** (`/stations/{id}/library`):
drag and drop or choose files, one upload at a time with its progress
(announced to screen readers), the storage used against the quota (also on the
Account usage card), and the tracks with their duration, size and format.
Unsupported, oversized and over-quota files are refused before uploading; what
the server refuses shows its reason. The page reminds that the station must
hold the rights to the music (Terms of Service). Plans without AutoDJ see an
upgrade note instead.

## Upload

1. `POST /api/v1/stations/{id}/tracks/uploads` with `fileName` and `size`.
   The extension must be `.mp3`, `.flac`, `.ogg`, `.oga` or `.opus`, the size
   at most 200 MB, and the declared size must fit the quota. The answer has a
   presigned `uploadUrl`, the `headers` to send (`Content-Type` and
   `If-None-Match: *`), `expiresAt` and the `trackId`. A pending track row
   holds the declared size in the quota.
2. The browser PUTs the file to `uploadUrl` with those headers, starting before
   `expiresAt` (30 minutes). The URL is signed for them and for a body of
   exactly the declared size: the bucket refuses another content type, another
   length, and any PUT once the object exists. So the quota counts what is
   stored, and a checked file cannot be replaced through its URL.
3. `POST /api/v1/stations/{id}/tracks` with `uploadId` (and optional `title`,
   `artist`). The server reads the stored object, not the client's claims:
   its size (HEAD), then its first 256 KB and last 64 KB (`AudioProbe`) for the
   format, duration, sample rate and tags. MP3 (CBR or Xing/Info/VBRI VBR),
   FLAC, and Ogg Opus or Vorbis are accepted, up to 3 hours. A file that is
   not readable audio or empty (422), too large, or over the quota at its real
   size (403) is deleted from the bucket at once. Its pending row stays until it
   expires, holding the declared size, so a file PUT again to the URL is
   deleted by the expiry cleanup. `409` means the file has not arrived yet
   (confirm again), or the upload was confirmed or expired meanwhile.

An MP3 whose ID3v2 tag (cover art) runs past the first 256 KB gets a second
read where the tag ends, for its first frames. An MP3 must start with three
consecutive frames of the same version and sample rate; WAV, AIFF and MP4
containers are refused.

Title and artist come from the request, else the file's tags (ID3v2, ID3v1,
Vorbis comments), else the file name.

## Quota

`Plan.StorageQuotaBytes` is per workspace, across its stations: Free 0 (no
AutoDJ), Starter 1 GB, Growth 5 GB, Pro 20 GB. Confirmed tracks and pending
uploads count. The tenant (`storageUsedBytes`), the billing usage (`storage`)
and the library (`storage`) report it. The workspace row is locked while
checking, so concurrent uploads cannot pass the quota together.

## Deletion

| What                    | How                                                                                                                                       |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| One track               | `DELETE /api/v1/stations/{id}/tracks/{trackId}`: object, then row                                                                         |
| A station               | `StationDeleted` → `StationTracksCleanup`: every object under `stations/{id}/`, then the rows; pending uploads stay for the cleanup below |
| An unconfirmed upload   | `PendingUploadCleanup`, every 10 minutes, 90 minutes after the start: the URL's 30 minutes, plus an hour for a PUT still running          |
| An account or workspace | Needs its stations deleted first, so the station cleanup covers it                                                                        |

Object keys are `stations/{station id}/tracks/{track id}` (no user data in the
key). Music rights are the station's responsibility (Terms of Service).
