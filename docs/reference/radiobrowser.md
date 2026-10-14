# RadioBrowser listing

Station owners opt in per station (_List on RadioBrowser_, Starter plan and
above). The station must have a country, which RadioBrowser requires.

- **Add once.** `DirectoryWorker` (when `Directory:Enabled`, production only)
  adds an opted-in station the first time it is live: `POST /json/add` on a
  RadioBrowser server, found from `all.api.radio-browser.info` and tried in
  random order. It sends the public MP3 listener URL, name, tags, country,
  language, logo and homepage (the website, else the public page). It stores
  the returned UUID and never adds that station again.
- **Updates through the stream.** RadioBrowser's API cannot edit or delete an
  entry. The listing metadata goes into the node's `stations.json` instead.
  The node sends it as `icy-*` headers with `icy-index-metadata: 1`, which
  RadioBrowser applies at its next check of the stream. Opting out sends
  `icy-do-not-index: 1`; RadioBrowser then marks the entry broken, which hides
  it from most apps (its API cannot delete it).
- **Never blocking.** Failures are retried with backoff (1 minute doubling to
  6 hours) and shown on the station page
  (`GET /api/v1/stations/{id}/directory`). Broadcasting and editing never wait
  for RadioBrowser.
- Only public data is sent: never ingest URLs, credentials or internal host
  names.
