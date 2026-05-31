import { describe, it, expect } from "vitest";
import { encryptSecret, base64UrlDecode, type PublishedKey, type SecretEnvelope } from "../src/index";

const subtle = globalThis.crypto.subtle;

function b64url(bytes: Uint8Array): string {
  let binary = "";
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]!);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** A fresh RSA-OAEP keypair, shaped into the server's PublishedKey document. */
async function makeKey(): Promise<{ key: PublishedKey; privateKey: CryptoKey }> {
  const pair = (await subtle.generateKey(
    { name: "RSA-OAEP", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" },
    true,
    ["encrypt", "decrypt"],
  )) as CryptoKeyPair;

  const spki = new Uint8Array(await subtle.exportKey("spki", pair.publicKey));
  return {
    key: {
      kid: "test",
      alg: "RSA-OAEP-AES256-GCM",
      walg: "RSA-OAEP-256",
      enc: "AES-256-GCM",
      format: "spki",
      encoding: "base64url",
      publicKey: b64url(spki),
    },
    privateKey: pair.privateKey,
  };
}

/** Mirror of the server's decryption: RSA-OAEP-SHA256 unwrap + AES-256-GCM open. */
async function decrypt(envelope: SecretEnvelope, privateKey: CryptoKey): Promise<string> {
  const dek = await subtle.decrypt({ name: "RSA-OAEP" }, privateKey, base64UrlDecode(envelope.wk));
  const aesKey = await subtle.importKey("raw", dek, { name: "AES-GCM" }, false, ["decrypt"]);

  const ct = base64UrlDecode(envelope.ct);
  const tag = base64UrlDecode(envelope.tag);
  const sealed = new Uint8Array(ct.length + tag.length);
  sealed.set(ct, 0);
  sealed.set(tag, ct.length);

  const plaintext = await subtle.decrypt(
    { name: "AES-GCM", iv: base64UrlDecode(envelope.iv), tagLength: 128 },
    aesKey,
    sealed,
  );
  return new TextDecoder().decode(plaintext);
}

describe("encryptSecret", () => {
  it("produces a well-formed cocoar.secret envelope", async () => {
    const { key } = await makeKey();
    const env = await encryptSecret(key, "super-secret-value");

    expect(env.type).toBe("cocoar.secret");
    expect(env.version).toBe(1);
    expect(env.kid).toBe("test");
    expect(env.alg).toBe("RSA-OAEP-AES256-GCM");
    expect(env.walg).toBe("RSA-OAEP-256");

    // All binary fields are base64url without padding.
    for (const field of [env.wk, env.iv, env.ct, env.tag]) {
      expect(field).toMatch(/^[A-Za-z0-9_-]+$/);
    }
  });

  it("round-trips a string (JSON-quoted plaintext, like the server)", async () => {
    const { key, privateKey } = await makeKey();
    const env = await encryptSecret(key, "super-secret-value");
    expect(await decrypt(env, privateKey)).toBe(JSON.stringify("super-secret-value"));
  });

  it("round-trips an object value", async () => {
    const { key, privateKey } = await makeKey();
    const value = { clientId: "abc", clientSecret: "xyz", scopes: ["a", "b"] };
    const env = await encryptSecret(key, value);
    expect(JSON.parse(await decrypt(env, privateKey))).toEqual(value);
  });

  it("uses a fresh IV per call (no nonce reuse)", async () => {
    const { key } = await makeKey();
    const a = await encryptSecret(key, "x");
    const b = await encryptSecret(key, "x");
    expect(a.iv).not.toBe(b.iv);
  });

  it("rejects an unsupported key-wrap algorithm", async () => {
    const { key } = await makeKey();
    await expect(encryptSecret({ ...key, walg: "RSA-OAEP-1" }, "x")).rejects.toThrow();
  });
});
