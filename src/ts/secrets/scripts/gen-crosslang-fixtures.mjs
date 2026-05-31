// Regenerates the cross-language golden fixture: a TEST RSA keypair + an envelope produced by the BUILT
// @cocoar/secrets library, written into the .NET test project. A .NET xunit test then decrypts the envelope
// and asserts the plaintext — proving the TS wire format is byte-compatible with the .NET decryptor.
//
// Run from src/ts:  pnpm --filter @cocoar/secrets gen:fixtures   (build first; this imports dist/)
//
// The private key is a throwaway TEST key — it is NOT used anywhere in production.

import { writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { encryptSecret } from "../dist/index.js";

const subtle = globalThis.crypto.subtle;
const here = dirname(fileURLToPath(import.meta.url));

const b64 = (buf) => Buffer.from(buf).toString("base64");
const b64url = (buf) => Buffer.from(buf).toString("base64url");

// Includes a non-ASCII character to exercise UTF-8 round-trip across the stacks.
const PLAINTEXT = "cross-language-secret-✓";

const pair = await subtle.generateKey(
  { name: "RSA-OAEP", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" },
  true,
  ["encrypt", "decrypt"],
);

const spki = new Uint8Array(await subtle.exportKey("spki", pair.publicKey));
const pkcs8 = new Uint8Array(await subtle.exportKey("pkcs8", pair.privateKey));

const publishedKey = {
  kid: "crosslang-test",
  alg: "RSA-OAEP-AES256-GCM",
  walg: "RSA-OAEP-256",
  enc: "AES-256-GCM",
  format: "spki",
  encoding: "base64url",
  publicKey: b64url(spki),
};

const envelope = await encryptSecret(publishedKey, PLAINTEXT);

const fixture = {
  _comment:
    "TEST-ONLY cross-language fixture (TS encrypt -> .NET decrypt). privateKeyPkcs8 is a throwaway test key, " +
    "never used in production. Regenerate: pnpm --filter @cocoar/secrets gen:fixtures (from src/ts).",
  plaintext: PLAINTEXT,
  privateKeyPkcs8: b64(pkcs8),
  publishedKey,
  envelope,
};

const out = join(
  here,
  "..", "..", "..",
  "tests", "Cocoar.Configuration.Secrets.Tests", "CrossLang", "ts-envelope.fixture.json",
);
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, JSON.stringify(fixture, null, 2) + "\n");
console.log("Wrote cross-language fixture:", out);
