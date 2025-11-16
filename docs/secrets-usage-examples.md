# Secrets Usage Examples

## Core Principle

**Secrets are decrypted on-demand only.** The Secrets system expects pre-encrypted envelopes in configuration. Use the `cocoar-secrets` CLI tool or external encryption systems (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) to encrypt secrets before they reach your application.

## Production Usage (File-Based Certificate)

**Recommended for production**: Uses a PFX file for decrypting pre-encrypted secrets.

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        .UseCertificateFromFile("config/secrets.pfx", "YourSecurePassword123!")
        .WithKeyId("my-app-cert")
]).Initialize();
```

**How it works:**
1. **Encryption** (external): Use `cocoar-secrets encrypt` CLI tool or CI/CD pipeline to pre-encrypt secrets
2. **Configuration**: Pre-encrypted envelopes are stored in JSON files
3. **Decryption** (runtime): Secrets are decrypted on-demand when you call `.Open()` using the certificate's private key

**Important:** 
- Certificate must exist - no auto-creation in production
- Store the PFX file securely and back it up
- Use a strong password
- Add the PFX file to `.gitignore` to avoid committing it
- Secrets must be pre-encrypted with matching `kid` (key identifier)

## Development Usage with Auto-Generated Certificate

**For development/testing**: Auto-create certificate if it doesn't exist.

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        .UseCertificateFromFile("config/dev-secrets.pfx", "DevPassword123!")
        .CreateSelfSignedIfNotExist("CN=My App Dev Secrets")
        .WithKeyId("dev-cert")
]).Initialize();
```

**Benefits:**
- First run auto-creates the certificate
- Subsequent runs reuse the same certificate
- Enables local development without manual cert generation
- Secrets encrypted with this cert can be decrypted across restarts

**Note:** For quick throwaway tests, manually generate using `X509CertificateGenerator.GenerateAndSave()` before creating ConfigManager.

## Multiple Managers with Separate Certificates

Each manager can have its own certificate file:

```csharp
// Manager 1 - App A
var managerA = new ConfigManager(rulesA, setup => [
    setup.Secrets()
        .UseCertificateFromFile("config/app-a-secrets.pfx", "PasswordA")
        .WithKeyId("app-a-cert")
]).Initialize();

// Manager 2 - App B
var managerB = new ConfigManager(rulesB, setup => [
    setup.Secrets()
        .UseCertificateFromFile("config/app-b-secrets.pfx", "PasswordB")
        .WithKeyId("app-b-cert")
]).Initialize();
```

**Result**: Each manager has completely isolated decryption keys.

## Sharing Certificates Across Managers

To share the same certificate (for decrypting shared secrets):

```csharp
var sharedPfx = "config/shared-secrets.pfx";
var sharedPassword = "SharedPassword123!";

var manager1 = new ConfigManager(rules1, setup => [
    setup.Secrets()
        .UseCertificateFromFile(sharedPfx, sharedPassword)
        .WithKeyId("shared-cert")
]).Initialize();

var manager2 = new ConfigManager(rules2, setup => [
    setup.Secrets()
        .UseCertificateFromFile(sharedPfx, sharedPassword)
        .WithKeyId("shared-cert")
]).Initialize();
```

**Result**: Both managers can decrypt secrets encrypted with the same certificate.

## Using Existing PFX File (Production Pattern)

Standard approach when certificate already exists:

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        .UseCertificateFromFile("existing-cert.pfx", "ExistingPassword")
        .WithKeyId("existing-cert")
]).Initialize();
```

**Note**: This will throw an exception if the file doesn't exist (fails fast for production safety).

## Certificate Discovery from Folder

For advanced scenarios with multiple certificates or certificate rotation:

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        .UseCertificatesFromFolder(
            basePath: "certs",
            passwordProvider: ctx => new[] { "Password123!" },
            searchPattern: "*.pfx",
            cacheDurationSeconds: 30)
]).Initialize();
```

**Features:**
- Supports kid-based subdirectories: `certs/{kid}/certificate.pfx`
- Automatic certificate caching with configurable duration
- Multiple certificate formats (PFX, PEM)
- See [Intelligent Certificate Caching](intelligent-certificate-caching.md) for details

## Configuration with Multiple Protectors (Certificate Rotation)

You can register multiple protectors for seamless certificate rotation:

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        // Old certificate (for reading legacy secrets)
        .UseCertificateFromFile("config/old-cert.pfx", "OldPassword")
        .WithKeyId("old-cert")
        .Build()
        // New certificate (for reading current secrets)
        .UseCertificateFromFile("config/new-cert.pfx", "NewPassword")
        .WithKeyId("new-cert")
]).Initialize();
```

**Result**: Can decrypt secrets encrypted with either certificate.

**Certificate Rotation Strategy:**
1. Generate new certificate with new `kid`
2. Register both old and new certificates (as shown above)
3. Re-encrypt secrets using new certificate (`cocoar-secrets encrypt` with new cert)
4. After transition period, remove old certificate configuration

## How Pre-Encrypted Secrets Work

When you use `Secret<T>` in your configuration type:

1. **Encryption (external)** → Use `cocoar-secrets` CLI or external system to pre-encrypt secrets
2. **Storage** → Encrypted envelopes are stored in JSON files with `_cocoar_secret` marker
3. **Loading** → ConfigManager recognizes the envelope format and stores it as-is
4. **Decryption (on-demand)** → You call `.Open()` on the secret to decrypt it only when needed

**Example:**

```csharp
public class AppConfig
{
    public string Username { get; init; } = string.Empty;
    public Secret<string> Password { get; init; }  // Pre-encrypted in JSON
}

// Input JSON (after encryption with cocoar-secrets CLI)
// {
//   "Username": "admin",
//   "Password": {
//     "_cocoar_secret": "v1",
//     "alg": "RSA-OAEP-AES256-GCM",
//     "kid": "my-cert",
//     "iv": "...",
//     "ct": "...",
//     "tag": "...",
//     "wk": "..."
//   }
// }

// Usage
var config = manager.GetRequiredConfig<AppConfig>();
Console.WriteLine(config.Username);  // "admin" - plain text
using var passwordLease = config.Password.Open();  // Decrypts on-demand
Console.WriteLine(passwordLease.Value);  // "secret123" - decrypted plaintext
// Memory automatically zeroized after 'using' block
```

**Key Points:**
- Secrets must be pre-encrypted using `cocoar-secrets` CLI tool or external encryption system
- ConfigManager never encrypts - only decrypts pre-encrypted envelopes
- Decryption happens on-demand when you call `.Open()`
- Encrypted envelopes are identified by `_cocoar_secret` marker and matching `kid` (key identifier)
- Use `using` statement to ensure automatic memory cleanup
