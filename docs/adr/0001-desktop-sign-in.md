# ADR 0001: Desktop sign-in with the device authorization flow

- Status: accepted
- Date: 2026-10-10
- Issue: #9

## Context

The desktop app (tc-station) must sign in to the control plane to list the
user's stations and get a broadcast credential. Two options were open:

1. The password grant: the app asks for email and password and calls
   `/auth/token` (#4).
2. The OAuth 2.0 device authorization flow (RFC 8628): the app shows a short
   code; the user approves it on the web, where they are already signed in.

## Decision

Use the device authorization flow (`/api/v1/auth/device/*`). `/auth/token`
stays for scripts and tests.

- The app never handles the user's password. Password managers, a future
  second factor, passkeys or social sign-in all work in the browser without
  any app change.
- The user sees the device name before approving and can deny it.
- Tokens are the same as `/auth/token`: a 15-minute access token and a
  rotating refresh token bound to a device session, so sign-out and the
  *Devices* list work the same way.

Details:

- Device code: `{request id}.{256-bit secret}`; only the SHA-256 hash is stored,
  compared in constant time; it gives tokens once.
- User code: 8 letters from 20 consonants (about 2.6 × 10¹⁰ codes), valid for
  10 minutes, approved only by a browser session (cookie + antiforgery; a
  device's bearer token cannot approve another device), behind
  the per-IP sign-in rate limit.
- Polling: at most every 5 seconds (`slow_down` otherwise). It is not behind
  the sign-in rate limit; the 256-bit device code cannot be guessed.
- Expired requests are deleted every hour, and whenever a sign-in starts.
- JSON bodies and Problem Details with an RFC 8628 `error` member, instead of
  form-encoded OAuth requests: there is one first-party client and no
  `client_id`.

## Consequences

- The web app needs a `/device` page that shows the request and approves or
  denies it (#11). Until then, approval is through the API.
- First sign-in needs a browser.
