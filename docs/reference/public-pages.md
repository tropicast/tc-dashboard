# Public station page and embed

Each station of an active tenant has a public page and, on plans with
embedding (Growth, Pro), an embeddable player. The API renders them as plain
HTML: no SPA bundle, inline CSS, and a player script of a few kilobytes. A page
is about 10 KB before audio, and search engines and link previews see its
title and Open Graph tags.

| Address                                          | What                                                                                                                                      |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `/listen/{tenant-slug}/{station-slug}`           | Public page: name, logo, genre, description, live state, player. Station slugs are unique per tenant only, so the address names both.     |
| `/embed/{tenant-slug}/{station-slug}`            | Compact player for an `<iframe>`; `403` when the plan has no embedding.                                                                   |
| `/embed.js`                                      | `<script src="…/embed.js" data-station="{tenant}/{station}" async>` inserts the iframe after itself.                                      |
| `GET /api/v1/public/stations/{tenant}/{station}` | Public data, live state and stream URLs; `Cache-Control: public, max-age=15`, readable from any origin. No tenant, plan or listener data. |

The player plays the HTTPS listener URL directly (audio never goes through the
API). It picks Ogg Opus when the browser supports it, else MP3, and reconnects
with backoff (1 s doubling to 30 s). A strict Content-Security-Policy allows
only the page's own inline code (nonce), audio from the listener host and
images over HTTPS. Only `/embed/*` may be framed. The dashboard's station page
has the copy-ready links and snippets.

Browser check against a running stack with a live station (MP3 and Opus):
`REAL_E2E_PLAYER=1 PUBLIC_STATION_PATH=/listen/<tenant>/<station> npx playwright test e2e/public-player.spec.ts`
(in `web/`).
