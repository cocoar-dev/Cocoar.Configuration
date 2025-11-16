# Provider guidance: plaintext and pre-encrypted secrets

This guide shows how a provider can send data to Cocoar.Configuration, either as plaintext (for automatic encryption) or as pre-encrypted envelopes we can store and decrypt locally.

## Plaintext (simple, good for MVP)

- Prefer binary for sensitive values so we can zeroize buffers: supply base64 for `SecretBytes` properties.
- Example JSON for a config type with `SecretString` and `SecretBytes`:

```json
{
  "User": "sa",
  "Password": "p@ssw0rd!",           // SecretString (plaintext, will be automatically encrypted)
  "ApiToken": "AQIDBAU="            // SecretBytes (base64 for bytes 01 02 03 04 05)
}
```

- ConfigManager will automatically encrypt `Secret*` properties and store envelopes at commit time. Plaintext never persists in our snapshot.

## Pre-encrypted envelopes (store-as-is, decrypt on use)

When you want the provider to encrypt “from the start,” emit one of these envelopes per secret value. ConfigManager stores the object as-is and decrypts locally upon `Secret*.Open()`.

### AES-GCM envelope (shared symmetric key)

- You and the app share a 32-byte AES key identified by `kid`.
- Provider encrypts value with AES-GCM; app loads the key once at startup and decrypts locally.

```json
{
  "__cocoar_secret__": "v1",
  "alg": "A256GCM",
  "kid": "aes:config-v1",
  "type": "bytes",                   // or "utf8"
  "createdAt": "2025-10-24T08:12:34Z",
  "iv": "...base64url...",
  "ct": "...base64url...",
  "tag": "...base64url..."
}
```

Notes:
- type: "utf8" for text secrets (SecretString), "bytes" for binary (SecretBytes).
- kid: A stable identifier mapped to the key registered in `SecretsRuntime`.

### X.509 hybrid envelope (public-key wrap + AES-GCM)

- Provider has only the public certificate; app holds the private key locally.
- Provider generates a random 32-byte DEK, encrypts content with AES-GCM and wraps the DEK with RSA-OAEP-256 using the public key.

```json
{
  "__cocoar_secret__": "v1",
  "alg": "RSA-OAEP-256+A256GCM",
  "kid": "x509:thumbprint:ABCD1234...", // or a subject-based id you map
  "type": "utf8",                        // or "bytes"
  "createdAt": "2025-10-24T08:12:34Z",
  "iv": "...base64url...",
  "ct": "...base64url...",
  "tag": "...base64url...",
  "wk": "...base64url...",               // wrapped DEK
  "walg": "RSA-OAEP-256"
}
```

Notes:
- The app registers an `X509HybridSecretProtector` using the local private cert; decrypt is fully local, no cloud call.
- Provider never sees a decryption secret—just the public key.

## Rotation notes (applies to both AES and X.509)

- Use a new `kid` for each rotation (e.g., `aes:config-v2`, `x509:thumbprint:EFGH5678`).
- Providers encrypt new secrets with the new kid.
- Apps register both old and new protectors during the overlap period (dual-read).
- Optional: re-encrypt existing stored values with the new kid; otherwise, old values remain decryptable until you remove the old protector.
- After the deprecation window, remove the old protector.

## Merging across providers

- Multiple providers can emit the same key path; the configured rule order picks the winner.
- As long as the winner’s value is a valid envelope or plaintext for a `Secret*` property, ConfigManager will handle it correctly (no double-encryption).
