# Roadmap

Cocoar.Configuration is the open-source foundation — fully functional today for configuration, feature flags, entitlements, and secrets. Here's what we're building next.

## At a Glance

| Initiative | Status | Impact |
|---|---|---|
| [ConfigHub](/roadmap/confighub) | In Design | Management portal for config, secrets, and flags at scale |
| [Cloud Providers](/roadmap/cloud-providers) | Planned | Azure Key Vault, AWS Secrets Manager |
| [Database Provider](/roadmap/database-provider) | Planned | Tenant-specific config from SQL |

## Priority Order

We don't publish specific dates — we ship when it's ready. Rough priority:

1. **Cloud Providers** (Azure Key Vault + AWS) — door openers for enterprise adoption
2. **Database Provider** — unlocks tenant-specific config from SQL
3. **ConfigHub private preview** — management portal for at-scale deployments

## Current Limitations

These are things the library does not do today:

- **No management UI** — you manage config files, environment variables, and JSON directly. [ConfigHub](/roadmap/confighub) will address this.
- **No cloud KMS integration** — Azure Key Vault and AWS Secrets Manager require a [custom provider](/guide/providers/custom) today. [Native providers](/roadmap/cloud-providers) are planned.
- **No database provider** — tenant config from SQL requires a custom provider. [Native SQL support](/roadmap/database-provider) is planned.

## Open Source Commitment

The library is and stays **Apache-2.0**. Everything needed to run Cocoar.Configuration on your own is free, forever. ConfigHub is a separate commercial product for teams that need operational tooling at scale — the library does not require it.
