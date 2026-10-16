# Desktop app downloads and the welcome email

Users download Tropicast Station from the dashboard's own address (#127). The files stay in the
private tracks bucket, under `releases/tc-station/latest/`, where tc-station's `mirror-release.yml`
puts the latest published release (tc-station#72). Only that version is kept.

## Endpoints

| Endpoint | Auth | Answer |
|---|---|---|
| `GET /api/v1/downloads/station` | none (public rate limit) | Version, GitHub releases page, and per file: platform, name, size, sha256, link. `Cache-Control: public, max-age=300` |
| `GET /download/station/{platform}` | none (public rate limit) | **302** to a 10-minute presigned GET that saves the file under its name (`response-content-disposition`). `Cache-Control: no-store` |

`platform` is `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`, `certificate` (`tropicast.cer`),
`checksums` (`SHA256SUMS.txt`), `script` (`install-windows-msix.ps1`) or `readme` (install guide);
anything else is 404. The bytes never pass through the API; the link people see and share is
`https://app.tropicastradio.com/download/station/<platform>` and never expires.

The API reads `releases/tc-station/latest/manifest.json` (at most 64 KiB), keeps it 5 minutes, and
retries after 30 seconds when it is missing or unreadable. It serves only entries whose name is a
plain file name and whose platform is in the list above (the first one per platform).

**Fallback.** No bucket, no mirrored release, or storage down: the list answers `version: null`
with no files, and every `/download/station/...` link redirects to
`https://github.com/tropicast/tc-station/releases/latest`. A warning is logged
("No tc-station release in the bucket" or "manifest cannot be read").

## Web app

The *Station ready* onboarding step and the *Desktop app* card of each station show a download
button for the visitor's system (from the user agent; a Mac gets the Apple silicon build), the other
systems, the version and the install guide; or a GitHub link in the fallback case.

## Welcome email

Creating a workspace (`POST /api/v1/tenants`) raises `TenantCreated`; the `WelcomeEmail` outbox
consumer sends its first Owner "Your Tropicast workspace … is ready": a link to create the first
station, the four installer links with the version, the install guide, and the sign-in step
(`/device`). Through the outbox, so a mail failure retries later and never fails the creation.
Joining a workspace (invitation) sends nothing. Without a mirrored release, the email links to the
GitHub releases page. English only, like the other emails.
