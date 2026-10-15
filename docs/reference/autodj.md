# AutoDJ

AutoDJ plays a station's playlist when no DJ is live (#103). Tracks come from
the [media library](media-library.md); playlists and the per-station switch
are below. The streaming side (fallback mounts, Liquidsoap playout) is
tc-streaming#61.

## Playlists

A playlist belongs to one station and lists its confirmed tracks in play
order (a track may appear more than once, at most 2000 entries). `shuffle`
plays them in random order. Members of the workspace (Broadcaster and up)
manage them:

| Endpoint                                           | Notes                                                            |
| -------------------------------------------------- | ---------------------------------------------------------------- |
| `GET /stations/{id}/playlists`                     | Name, order mode, entry count, total duration                    |
| `POST /stations/{id}/playlists`                    | `name`, `shuffle`; starts empty                                  |
| `GET /stations/{id}/playlists/{playlistId}`        | Entries in play order; `ETag`                                    |
| `PATCH /stations/{id}/playlists/{playlistId}`      | `name`, `shuffle`; optional `If-Match`                           |
| `PUT /stations/{id}/playlists/{playlistId}/tracks` | `trackIds` in play order: adds, removes and reorders in one call |
| `DELETE /stations/{id}/playlists/{playlistId}`     | Refused (409) while AutoDJ plays it                              |

Only the station's own confirmed tracks can be added (400 otherwise).
Deleting a track removes it from every playlist.

## In the dashboard

The station page has an **AutoDJ** card: the switch, the playlist, what is on
air now (live DJ, AutoDJ or off air) and the encoding AutoDJ uses. **Edit
playlists** (`/stations/{id}/playlists`) creates, renames, shuffles and deletes
playlists, adds tracks from the media library and reorders them with Move up
and Move down buttons (keyboard focus follows the moved track; each change is
announced to screen readers). Plans without AutoDJ see an upgrade prompt.

## The AutoDJ switch

`GET /stations/{id}/autodj` (Member) and `PUT /stations/{id}/autodj` (Admin)
with `enabled` and `defaultPlaylistId`:

- Turning it on needs a plan with AutoDJ (`403` with "AutoDJ is not in the
  Free plan…" otherwise) and a default playlist with at least one track (`409`).
  Turning it off works on any plan.
- `active` is `enabled` and the plan includes AutoDJ: a downgrade keeps the
  choice but stops playback; an upgrade resumes it.
- The playlist AutoDJ plays cannot be deleted or emptied (409). Deleting its
  last track turns AutoDJ off instead of leaving it silent (audited, with the
  reason).
- Turning it on or off is audited (`autodj.enabled`, `autodj.disabled`);
  changing the playlist records `autodj.updated`. Every change raises
  `AutoDjChanged` through the outbox, for provisioning and the playout worker.

`scheduling` tells whether the plan includes scheduled playlists (Growth and
up). Schedules are not offered yet: every AutoDJ plays its one default
playlist.

## Encoding and credential

Icecast moves listeners between `live.<fmt>` and `autodj.<fmt>` without
reconnecting them, so both must have the same codec, bitrate, sample rate and
channels (#106).

- **Pinned from the live source.** Each live connect that declares
  `Ice-Audio-Info` (`bitrate`, `samplerate`, `channels`, as tc-station does)
  pins that encoding for its format (`encoding_profiles`). Until a live source
  connected, the defaults are what tc-station publishes for an account: MP3
  128 kbps at 44.1 kHz, Opus 64 kbps at 48 kHz, stereo, capped by the plan.
  `GET /stations/{id}/autodj` shows them (`encoding`, `pinned`); a downgrade
  caps the bitrate. The desktop app keeps its plan limits; nothing is forced on
  it.
- **AutoDJ credential.** Turning AutoDJ on issues the station's AutoDJ source
  credential (`broadcast_credentials.purpose = AutoDj`, one active per
  station); turning it off revokes it, so the next turn-on rotates it. Deleting
  the station revokes it too. It is not listed or revocable with the device
  credentials. The secret is kept encrypted with the API's Data Protection keys
  (cleared on revocation) so the playout worker can be given it (#107); source
  auth only compares its hash ([source auth](source-auth.md)).
