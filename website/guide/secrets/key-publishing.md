# Publishing Encryption Keys <Badge type="info" text="ADV" />

Secrets are encrypted with the **public** half of an X.509 certificate and decrypted server-side with the private half (see [Encryption Setup](/guide/secrets/encryption-setup)). To let an **external producer** — a browser form, a CLI, another service — build a `cocoar.secret` envelope your server can later decrypt, you publish the **public key** over an HTTP endpoint.

Only public-key material is ever exposed. The private key never leaves the server, and no plaintext is reachable through this API. Each endpoint returns **exactly one key** — never a list — so one tenant's key can never expose another's.

## Single-tenant

When secrets are configured with one current key (single-kid mode via `UseCertificateFromFile`), map the single-key endpoint:

```csharp
app.MapSecretEncryptionKey();   // GET /.well-known/cocoar/encryption-key
```

| Route | Returns |
|---|---|
| `GET /.well-known/cocoar/encryption-key` | the current public key, or `404` ProblemDetails when nothing is publishable |

Pass a custom pattern if the default route doesn't fit:

```csharp
app.MapSecretEncryptionKey("/keys/cocoar");
```

## Multi-tenant

In multi-tenant deployments each tenant has its own certificate(s) under a `kid = tenant` subfolder (`basePath/{tenant}/cert.pfx`, configured with `UseCertificatesFromFolder`). The per-tenant endpoint returns **only the current key of the tenant the request already resolves to** — it never lists keys and never exposes another tenant:

```csharp
app.MapTenantSecretEncryptionKey();   // GET /.well-known/cocoar/encryption-key
```

The tenant is read from `ITenantContext.Current` — your app supplies it from auth, subdomain, or route (the same seam used by [scoped tenant config](/guide/multi-tenancy/overview)), never from a client-chosen value. Register it via `AddCocoarTenantResolver<TService>(s => s.TenantId)` (HTTP: `AddCocoarTenantResolver<IHttpContextAccessor>(...)`) or your own scoped `ITenantContext`.

| Route | Returns |
|---|---|
| `GET /.well-known/cocoar/encryption-key` | the resolved tenant's current public key; `404` when that tenant has none; `400` when no tenant is resolved |

::: warning Not secured by default
Like `MapFeatureFlagEndpoints`, these routes are **open** unless you secure them. Public keys are safe to expose, but to put them behind auth chain `.RequireAuthorization()`:

```csharp
app.MapTenantSecretEncryptionKey().RequireAuthorization();
```
:::

## Response shape

The endpoint returns the current public key directly (no list wrapper):

```json
{
  "kid": "prod-secrets",
  "alg": "RSA-OAEP-AES256-GCM",
  "walg": "RSA-OAEP-256",
  "enc": "AES-256-GCM",
  "format": "spki",
  "encoding": "base64url",
  "publicKey": "<base64url DER SubjectPublicKeyInfo, no padding>"
}
```

Every field name is pinned, so a host JSON naming policy can't rename it. There is exactly **one current key per tenant** — the **newest certificate** in that tenant's set (per the configured certificate comparer; the default orders by file name). Older certificates stay available for **decryption only** (rotation). Key material is re-read on every request, so adding a newer certificate is reflected without a restart.

## How a producer uses it

1. Fetch the current key. `alg` / `walg` / `enc` describe the scheme; `publicKey` is the SPKI to import.
2. Generate a random AES-256 DEK, encrypt the value with AES-GCM, wrap the DEK with RSA-OAEP-256, and assemble the `cocoar.secret` envelope (with `kid` stamped from the key).
3. Send the envelope to your server. It is stored as-is and decrypted only on `Secret<T>.Open()`.

The envelope wire format is documented in [Custom Providers → Secrets](/guide/providers/custom#secrets-in-custom-providers). The same envelope can be written through a WritableStore overlay via `SetSecretEnvelopeAsync` / `SetSecretAsync` — including per tenant with `GetWritableStoreForTenant<T>(tenantId).SetSecretAsync(...)`, which is how a tenant stores a secret encrypted to its own published key.

## Availability

Publishing is available when secrets are configured via [`UseSecretsSetup`](/guide/secrets/encryption-setup):

- **Single-kid** (`UseCertificateFromFile`) publishes one key via `GetCurrentKey()` / `MapSecretEncryptionKey`.
- **Folder / multi-tenant** (`UseCertificatesFromFolder`, `kid = tenant`) publishes one key per tenant via `GetCurrentKeyForTenant(tenantId)` / `MapTenantSecretEncryptionKey`.

When no secrets are configured, the service is not registered and the endpoint returns `404`.

The DI service behind the endpoints is `ISecretEncryptionKeyProvider` (`GetCurrentKey()` / `GetCurrentKeyForTenant(tenantId)`), registered wherever secrets are configured — resolve it directly to build your own controller (e.g. one that already knows the tenant) or workflow.
