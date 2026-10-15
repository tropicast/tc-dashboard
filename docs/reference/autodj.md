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
