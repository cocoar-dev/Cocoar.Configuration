# Secrets Usage Examples

## Core Principle

**All `Secret*` properties are ALWAYS encrypted.** There is no option to disable encryption for secrets. If you use `SecretString` or `SecretBytes` in your config type, the system will automatically encrypt those values when storing them.

## Production Usage (File-Based Certificate)

**Recommended for production**: Uses a PFX file that persists across app restarts, allowing you to decrypt previously encrypted secrets.

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        .UseSelfSignedCertificate(
            pfxPath: "config/secrets.pfx",
            password: "YourSecurePassword123!",
            keyId: "my-app-cert")
]).Initialize();
```

**How it works:**
1. **First run**: If `secrets.pfx` doesn't exist, a new self-signed certificate is created and saved
2. **Subsequent runs**: The same certificate is loaded from the file
3. **Encryption**: All `Secret*` properties are automatically encrypted with the certificate's public key
4. **Decryption**: Secrets are decrypted on-demand when you call `.Open()` using the certificate's private key

**Important:** 
- If you delete the PFX file, all previously encrypted secrets become unreadable
- Store the PFX file securely and back it up
- Use a strong password
- Add the PFX file to `.gitignore` to avoid committing it

## Testing Usage (Ephemeral Certificate)

**For unit tests**: Use temporary PFX files in the temp directory with cleanup.

```csharp
var kid = $"test-{Guid.NewGuid():N}";
var pfxPath = Path.Combine(Path.GetTempPath(), $"{kid}.pfx");

try
{
    var manager = new ConfigManager(rules, setup => [
        setup.Secrets()
            .UseSelfSignedCertificate(pfxPath, "TestPassword123!", kid)
    ]).Initialize();
    
    // Your test logic
}
finally
{
    if (File.Exists(pfxPath))
        File.Delete(pfxPath);
}
```

**Benefits:**
- Each test gets a unique certificate
- Proper cleanup prevents test pollution
- Can test pre-encrypted envelope scenarios by reusing the same PFX file

## Multiple Managers with Separate Certificates

Each manager can have its own certificate file:

```csharp
// Manager 1 - App A
var managerA = new ConfigManager(rulesA, setup => [
    setup.Secrets()
        .UseSelfSignedCertificate("config/app-a-secrets.pfx", "PasswordA", "app-a-cert")
]).Initialize();

// Manager 2 - App B
var managerB = new ConfigManager(rulesB, setup => [
    setup.Secrets()
        .UseSelfSignedCertificate("config/app-b-secrets.pfx", "PasswordB", "app-b-cert")
]).Initialize();
```

**Result**: Each manager has completely isolated encryption keys.

## Sharing Certificates Across Managers

To share the same certificate (for decrypting shared secrets):

```csharp
var sharedPfx = "config/shared-secrets.pfx";
var sharedPassword = "SharedPassword123!";

var manager1 = new ConfigManager(rules1, setup => [
    setup.Secrets()
        .UseSelfSignedCertificate(sharedPfx, sharedPassword, "shared-cert")
]).Initialize();

var manager2 = new ConfigManager(rules2, setup => [
    setup.Secrets()
        .UseSelfSignedCertificate(sharedPfx, sharedPassword, "shared-cert")
]).Initialize();
```

**Result**: Both managers can encrypt and decrypt each other's secrets.

## Using Existing PFX File

If you already have a PFX certificate (won't create a new one):

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        .UseCertificateFromFile(
            pfxPath: "existing-cert.pfx",
            password: "ExistingPassword",
            keyId: "existing-cert")
]).Initialize();
```

**Note**: This will throw an exception if the file doesn't exist.

## Customizing Certificate Subject Name

When creating a new certificate, you can specify the subject name:

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        .UseSelfSignedCertificate(
            pfxPath: "config/secrets.pfx",
            password: "Password123!",
            keyId: "my-cert",
            subjectName: "CN=My Application Secrets, O=MyCompany, C=US")
]).Initialize();
```

## Configuration with Multiple Protectors

You can register multiple protectors (last one becomes default for writing):

```csharp
var manager = new ConfigManager(rules, setup => [
    setup.Secrets()
        // Old certificate (for reading old secrets)
        .UseCertificateFromFile("config/old-cert.pfx", "OldPassword", "old-cert")
        // New certificate (for writing new secrets)
        .UseSelfSignedCertificate("config/new-cert.pfx", "NewPassword", "new-cert")
]).Initialize();
```

**Result**: Can decrypt secrets encrypted with either certificate, but new secrets use the new certificate.

## How Automatic Encryption Works

When you use `SecretString` or `SecretBytes` in your configuration type:

1. **Plain text input** → System detects it's a `Secret*` property
2. **Automatic encryption** → Wraps the value in an encrypted envelope using the configured protector
3. **Storage** → Encrypted envelope is stored (never plain text)
4. **Retrieval** → You call `.Open()` on the secret to decrypt it on-demand

**Example:**

```csharp
public class AppConfig
{
    public string Username { get; init; } = string.Empty;
    public SecretString Password { get; init; } = SecretString.Empty;  // ALWAYS encrypted
}

// Input JSON (plain text)
// { "Username": "admin", "Password": "secret123" }

// After encryption (what's actually stored)
// { "Username": "admin", "Password": { "__cocoar_secret__": "v1", "alg": "RSA-OAEP-256+A256GCM", "kid": "...", "ct": "...", ... } }

// Usage
var config = manager.GetRequiredConfig<AppConfig>();
Console.WriteLine(config.Username);  // "admin" - plain text, no protection needed
using var passwordLease = config.Password.Open();  // Decrypts on-demand
Console.WriteLine(passwordLease.Value);  // "secret123" - decrypted
```

**Key Points:**
- No way to disable encryption for `Secret*` properties
- Encryption happens automatically during configuration processing
- Decryption happens on-demand when you call `.Open()`
- Plain text values are never persisted for `Secret*` properties
