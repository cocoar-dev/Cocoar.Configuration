---
description: '@cocoar/secrets TypeScript library — fetchEncryptionKey and encryptSecret build cocoar.secret envelopes client-side via WebCrypto so plaintext never reaches the server, multi-tenant'
---

# Browser & Client Encryption <Badge type="info" text="ADV" />

[Publishing Encryption Keys](/guide/secrets/key-publishing) exposes your server's **public** key so an external producer can build a `cocoar.secret` envelope. **`@cocoar/secrets`** is that producer for the browser and Node — a tiny, zero-dependency TypeScript library that encrypts a value with the published key so the **plaintext never reaches your server**; only the encrypted envelope does.

It pairs with the publishing endpoint: the server holds the private key and decrypts on `Secret<T>.Open()`; the client only ever sees the public key.

## Install

```bash
npm install @cocoar/secrets
```

Requires WebCrypto (`globalThis.crypto.subtle`) — every modern browser and Node 18+. No runtime dependencies.

## Usage

```ts
import { fetchEncryptionKey, encryptSecret } from "@cocoar/secrets";

// 1. Fetch the server's published public key.
const key = await fetchEncryptionKey("/.well-known/cocoar/encryption-key");

// 2. Encrypt a secret value (a string, or any JSON-serializable object).
const envelope = await encryptSecret(key, "my-oauth-client-secret");

// 3. POST the envelope to your API — the plaintext never left the browser.
await fetch("/admin/config/oauth-secret", {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify(envelope),
});
```

The server stores the envelope as-is — for example through a [WritableStore](/guide/providers/writable-store) overlay via `SetSecretEnvelopeAsync` / `SetSecretAsync` — and decrypts it only when the typed `Secret<T>` is opened.

## Multi-tenant

No client change is needed. The [per-tenant endpoint](/guide/secrets/key-publishing#multi-tenant) returns the current tenant's key, resolved server-side from `ITenantContext`. The browser fetches the same URL and gets the right key; the resulting envelope is stored against that tenant — e.g. `GetWritableStoreForTenant<T>(tenantId).SetSecretAsync(...)`.

## What it does

`encryptSecret` performs hybrid encryption matching the server's decryption contract:

- generates a one-time **AES-256-GCM** data key and a 96-bit IV,
- seals the JSON-serialized value with it (a string round-trips as a quoted JSON string),
- wraps the data key with the server's **RSA-OAEP-SHA256** public key,
- assembles a `cocoar.secret` envelope (all binary fields base64url, no padding).

The wire format is the same one under [Custom Providers → Secrets](/guide/providers/custom#secrets-in-custom-providers); a TS→.NET round-trip is covered by a cross-language test so the two stacks stay byte-compatible.

## API

| Export | Purpose |
|---|---|
| `fetchEncryptionKey(url, init?)` | fetch the published key document |
| `encryptSecret(key, value)` | seal a value into a `cocoar.secret` envelope |
| `base64UrlEncode` / `base64UrlDecode` | base64url (no padding) helpers |

## Versioning

`@cocoar/secrets` is published to npm and versioned **independently** of the .NET (NuGet) packages — it codes against the stable published-key contract, not a specific library release.
