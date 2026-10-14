# Changelog

All notable user-facing and operator-facing changes. Format: [Keep a Changelog](https://keepachangelog.com/);
versions follow [Semantic Versioning](https://semver.org/).

When releasing, rename `Unreleased` to the version (for example `## [0.2.0] - 2026-11-01`) and add a
new empty `Unreleased` section. The release workflow requires that section and publishes it above the
auto-generated list of merged pull requests. See [Releasing](docs/runbook.md#releasing).

## [Unreleased]

### Added

- Release workflow: a `v*` tag builds versioned images, checks the running API reports the version and drafts a GitHub Release from this changelog. The version comes from the tag only; the repository holds a `0.0.0` placeholder.
- Deploy a release by tag (`ref=v0.2.0`) as well as by commit.
- Public station page and embeddable player, with a dashboard-styled player card.
- RadioBrowser directory listing for public stations.
- Subscriptions and payments with Stripe (Free, Starter, Growth, Pro plans).
- Station owner dashboard with live listener metrics, egress and listening time.
- Desktop API: device sign-in (`/device` approval page) and broadcast target for Tropicast Station.
- Broadcast credentials per station and device: show once, list active ones, revoke with confirmation.
- Icecast source authentication endpoint for the streaming nodes.
- Provisioning worker that applies each station's listener limit on its streaming node.
- Tenants, members, invitations and stations REST API, with an audit log.
- Identity and authentication: SPA session cookie, desktop tokens, email confirmation.
- Dashboard UI on Untitled UI and Tailwind CSS, translated, with accessibility fixes (skip link, page titles, focus management, named charts and progress bars).
- Observability (OpenTelemetry to Grafana Cloud) and security hardening.
- Production infrastructure (Terraform, Ansible) and deploy pipeline with automatic rollback.
