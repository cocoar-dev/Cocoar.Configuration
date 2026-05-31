# Cocoar.Configuration — TypeScript clients

A pnpm + Turborepo monorepo for the publishable TypeScript client libraries that pair with
**Cocoar.Configuration**. Each package versions and publishes to npm independently.

## Packages

| Package | npm | Status |
|---------|-----|--------|
| [`secrets/`](./secrets) | `@cocoar/secrets` | encrypt secrets client-side so the server stores only an encrypted envelope and never sees the plaintext |
| `flags/` | `@cocoar/flags` | feature flags & entitlements client — _planned_ |

## Develop

```bash
# from src/ts
pnpm install
pnpm build     # turbo run build across all packages
pnpm test      # turbo run test across all packages
```

Adding a package: create `src/ts/<name>/` and add it to `pnpm-workspace.yaml`.
