# Publishing Encryption Keys <Badge type="info" text="ADV" />

Secrets are encrypted with the **public** half of an X.509 certificate and decrypted server-side with the private half (see [Encryption Setup](/guide/secrets/encryption-setup)). To let an **external producer** — a browser form, a CLI, another service — build a `cocoar.secret` envelope your server can later decrypt, you publish the **public key** over an HTTP endpoint.

Only public-key material is ever exposed. The private key never leaves the server, and no plaintext is reachable through this API.

## Mapping the endpoints (ASP.NET Core)

`Cocoar.Configuration.AspNetCore` maps the well-known endpoints:

```csharp
app.MapSecretEncryptionKeyEndpoints();   // list + by-kid, under /.well-known/cocoar/encryption-keys
```

This maps two routes and returns a single `IEndpointConventionBuilder`, so one convention (e.g. `.RequireAuthorization()`) covers both:

| Route | Returns |
|---|---|
| `GET /.well-known/cocoar/encryption-keys` | `{ "keys": [ … ] }` — the current public key per configured kid (always `200`; empty list when nothing is publishable) |
| `GET /.well-known/cocoar/encryption-keys/{kid}` | the public key for one kid, or `404` ProblemDetails when that kid is not published |

Map them individually instead if you only need one:

```csharp
app.MapSecretEncryptionKeys();        // just the list
app.MapSecretEncryptionKeyByKid();    // just the by-kid lookup
```

Pass a custom base pattern if the default route doesn't fit:

```csharp
app.MapSecretEncryptionKeyEndpoints("/keys/cocoar");
```

::: warning Not secured by default
Like `MapFeatureFlagEndpoints`, these routes are **open** unless you secure them. Public keys are safe to expose, but if you want them behind auth, chain `.RequireAuthorization()` — one call on the composite builder covers both routes:

```csharp
app.MapSecretEncryptionKeyEndpoints().RequireAuthorization();
```
:::

## Response shape

Each published key is the current public key for one `kid`:

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

The list endpoint wraps these as `{ "keys": [ … ] }`. The `keys` field name is pinned, so a host JSON naming policy can't rename it. There is exactly **one current key per kid** — the certificate the decryption engine prefers — and key material is re-read on every request, so certificate rotation is reflected without a restart.

## How a producer uses it

1. Fetch the key for the kid it should encrypt to. `alg` / `walg` / `enc` describe the scheme; `publicKey` is the SPKI to import.
2. Generate a random AES-256 DEK, encrypt the value with AES-GCM, wrap the DEK with RSA-OAEP-256, and assemble the `cocoar.secret` envelope (with `kid` stamped from the key).
3. Send the envelope to your server. It is stored as-is and decrypted only on `Secret<T>.Open()`.

The envelope wire format is documented in [Custom Providers → Secrets](/guide/providers/custom#secrets-in-custom-providers). The same envelope can be written through a LocalStorage overlay via `SetSecretEnvelopeAsync` / `SetSecretAsync`.

## Availability

Publishing is available when secrets are configured via [`UseSecretsSetup`](/guide/secrets/encryption-setup) with a single, unambiguous current key (single-kid mode). When nothing is publishable — no secrets configured — the list endpoint returns `{ "keys": [] }` and the by-kid endpoint returns `404`. Multi-kid / folder mode is decrypt-only for now, so it publishes nothing; per-kid (per-tenant) publishing is planned.

The DI service behind the endpoints is `ISecretEncryptionKeyProvider` (`GetCurrentKeys()` / `GetCurrentKey(kid)`), registered wherever secrets are configured — resolve it directly to build your own endpoint or workflow.
