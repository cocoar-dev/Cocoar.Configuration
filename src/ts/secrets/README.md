# @cocoar/secrets

Encrypt secrets for **Cocoar.Configuration** client-side — in the browser or Node — so the server
stores only an encrypted envelope and **never sees the plaintext**.

The server publishes the public half of its secrets encryption key; this library uses it to seal a
value with hybrid encryption (a one-time **AES-256-GCM** data key wrapped with the server's
**RSA-OAEP-SHA256** public key). The resulting `cocoar.secret` envelope is what you store (e.g. via a
WritableStore secret-write endpoint). Only the server, holding the private key, can decrypt it.

## Install

```bash
npm install @cocoar/secrets
```

Requires WebCrypto (`globalThis.crypto.subtle`) — every modern browser and Node 18+.

## Usage

```ts
import { fetchEncryptionKey, encryptSecret } from "@cocoar/secrets";

// 1. Fetch the server's published public key.
const key = await fetchEncryptionKey("/.well-known/cocoar/encryption-key");

// 2. Encrypt a secret value (string or any JSON-serializable object).
const envelope = await encryptSecret(key, "my-oauth-client-secret");

// 3. Send the envelope to your API — the plaintext never left the browser.
await fetch("/admin/config/oauth-secret", {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify(envelope),
});
```

For multi-tenant servers the same endpoint returns the current tenant's key (resolved server-side),
so no client changes are needed.

## API

- `fetchEncryptionKey(url, init?)` → `Promise<PublishedKey>` — fetch the published public key.
- `encryptSecret(key, value)` → `Promise<SecretEnvelope>` — seal a value into a `cocoar.secret` envelope.
- `base64UrlEncode` / `base64UrlDecode` — base64url (no padding) helpers.

## Wire format

The value is JSON-serialized (a string becomes a quoted JSON string, matching the server's
`JsonSerializer`), sealed with AES-256-GCM, and the data key is wrapped with RSA-OAEP-SHA256. All
binary fields are **base64url without padding**.

## License

Apache-2.0
