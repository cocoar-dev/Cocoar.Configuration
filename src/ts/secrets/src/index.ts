/**
 * `@cocoar/secrets` — encrypt a secret for **Cocoar.Configuration** so the server stores only an
 * encrypted envelope and never sees the plaintext.
 *
 * Hybrid encryption matching the server's decryption contract: a one-time **AES-256-GCM** data key
 * seals the value, and that key is wrapped with the server's **RSA-OAEP-SHA256** public key. Runs
 * anywhere WebCrypto is available — browsers and Node 18+ (`globalThis.crypto.subtle`).
 *
 * @example
 * ```ts
 * const key = await fetchEncryptionKey("/.well-known/cocoar/encryption-key");
 * const envelope = await encryptSecret(key, "my-oauth-client-secret");
 * await fetch("/admin/config/oauth-secret", { method: "POST", body: JSON.stringify(envelope) });
 * ```
 */

/** Overall hybrid scheme identifier (envelope `alg`). */
export const ALG_HYBRID = "RSA-OAEP-AES256-GCM";
/** Key-wrapping algorithm identifier (envelope `walg`). */
export const ALG_WRAP = "RSA-OAEP-256";
/** Data-encryption algorithm identifier (published key `enc`). */
export const ALG_ENC = "AES-256-GCM";

/** The public-key document the server publishes at `/.well-known/cocoar/encryption-key`. */
export interface PublishedKey {
  /** Key id (single-tenant: the configured kid; multi-tenant: the tenant id). */
  kid: string;
  /** Overall scheme — `"RSA-OAEP-AES256-GCM"`. */
  alg: string;
  /** Key-wrap algorithm — `"RSA-OAEP-256"`. */
  walg: string;
  /** Data-encryption algorithm — `"AES-256-GCM"`. */
  enc: string;
  /** Public-key structure — `"spki"` (X.509 SubjectPublicKeyInfo, DER). */
  format: string;
  /** Encoding of {@link publicKey} — `"base64url"` (no padding). */
  encoding: string;
  /** The RSA public key: DER SubjectPublicKeyInfo, base64url-encoded without padding. */
  publicKey: string;
}

/** The encrypted envelope the server accepts (`cocoar.secret`, version 1). */
export interface SecretEnvelope {
  type: "cocoar.secret";
  version: 1;
  kid: string;
  alg: string;
  /** base64url: the AES key wrapped with RSA-OAEP-SHA256. */
  wk: string;
  walg: string;
  /** base64url: the 96-bit AES-GCM IV. */
  iv: string;
  /** base64url: the ciphertext. */
  ct: string;
  /** base64url: the 128-bit GCM authentication tag. */
  tag: string;
}

function subtle(): SubtleCrypto {
  const c = (globalThis as { crypto?: Crypto }).crypto;
  if (!c?.subtle) {
    throw new Error(
      "WebCrypto is unavailable. Use a browser or Node 18+ where globalThis.crypto.subtle exists.",
    );
  }
  return c.subtle;
}

/** Encode bytes as base64url **without** padding (the wire format Cocoar expects). */
export function base64UrlEncode(bytes: Uint8Array): string {
  let binary = "";
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]!);
  }
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** Decode base64url (with or without padding) into bytes. */
export function base64UrlDecode(value: string): Uint8Array {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = normalized + "=".repeat((4 - (normalized.length % 4)) % 4);
  const binary = atob(padded);
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    out[i] = binary.charCodeAt(i);
  }
  return out;
}

function assertSupported(key: PublishedKey): void {
  if (key.format !== "spki") {
    throw new Error(`Unsupported public-key format '${key.format}' (expected 'spki').`);
  }
  if (key.encoding !== "base64url") {
    throw new Error(`Unsupported public-key encoding '${key.encoding}' (expected 'base64url').`);
  }
  if (key.walg !== ALG_WRAP) {
    throw new Error(`Unsupported key-wrap algorithm '${key.walg}' (expected '${ALG_WRAP}').`);
  }
  if (key.enc !== ALG_ENC) {
    throw new Error(`Unsupported data-encryption algorithm '${key.enc}' (expected '${ALG_ENC}').`);
  }
}

/** Fetch the server's published public key from the given URL. */
export async function fetchEncryptionKey(url: string, init?: RequestInit): Promise<PublishedKey> {
  const res = await fetch(url, init);
  if (!res.ok) {
    throw new Error(`Failed to fetch encryption key from ${url}: HTTP ${res.status}.`);
  }
  return (await res.json()) as PublishedKey;
}

/**
 * Encrypt a value into a Cocoar secret envelope using the server's published public key.
 *
 * The value is JSON-serialized first (a string becomes a quoted JSON string, matching the server's
 * `JsonSerializer` round-trip), then sealed with a one-time AES-256-GCM key that is wrapped with the
 * server's RSA-OAEP-SHA256 public key. The plaintext never leaves the caller in clear form.
 */
export async function encryptSecret(key: PublishedKey, value: unknown): Promise<SecretEnvelope> {
  assertSupported(key);
  const crypto = subtle();

  const plaintext = new TextEncoder().encode(JSON.stringify(value));

  // One-time AES-256-GCM data-encryption key + 96-bit IV.
  const dek = globalThis.crypto.getRandomValues(new Uint8Array(32));
  const iv = globalThis.crypto.getRandomValues(new Uint8Array(12));
  const aesKey = await crypto.importKey("raw", dek, { name: "AES-GCM" }, false, ["encrypt"]);
  const sealed = new Uint8Array(
    await crypto.encrypt({ name: "AES-GCM", iv, tagLength: 128 }, aesKey, plaintext),
  );

  // WebCrypto returns ciphertext || tag concatenated; the server expects them as separate fields.
  const ct = sealed.slice(0, sealed.length - 16);
  const tag = sealed.slice(sealed.length - 16);

  // Wrap the AES key with the server's RSA-OAEP-SHA256 public key. (Cast to BufferSource: TS 5.7+ types a
  // bare Uint8Array as Uint8Array<ArrayBufferLike>, which WebCrypto's BufferSource doesn't accept — the public
  // base64UrlDecode return type stays a plain Uint8Array so older-TS consumers aren't forced onto the generic.)
  const rsaPublicKey = await crypto.importKey(
    "spki",
    base64UrlDecode(key.publicKey) as BufferSource,
    { name: "RSA-OAEP", hash: "SHA-256" },
    false,
    ["encrypt"],
  );
  const wrappedKey = new Uint8Array(await crypto.encrypt({ name: "RSA-OAEP" }, rsaPublicKey, dek));

  // Best-effort: drop the plaintext key bytes once wrapped.
  dek.fill(0);

  return {
    type: "cocoar.secret",
    version: 1,
    kid: key.kid,
    alg: ALG_HYBRID,
    wk: base64UrlEncode(wrappedKey),
    walg: ALG_WRAP,
    iv: base64UrlEncode(iv),
    ct: base64UrlEncode(ct),
    tag: base64UrlEncode(tag),
  };
}
