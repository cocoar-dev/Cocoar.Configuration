using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers; // FromStaticJson / FromStore / IStoreBackend / GetWritableStoreForTenant
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;
using Cocoar.Configuration.X509Encryption;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.MultiTenant.Tests;

public sealed class VaultConfig
{
    public Secret<string>? ApiKey { get; set; }
}

/// <summary>
/// (Secrets) per-tenant secrets on the shared-base model with NO multi-tenancy-specific secrets code: the
/// existing multi-kid folder mode (kid = tenant subdirectory) routes decryption, and each tenant's overlay
/// carries an envelope tagged with its own kid. A tenant decrypts its own secret via its own cert; it cannot
/// decrypt another tenant's. Ports the intent of the POC's TenantSecretsPocTests to the real engine.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public sealed class TenantSecretsTests : IDisposable
{
    private readonly string _certsRoot = Path.Combine(Path.GetTempPath(), "cocoar-mt-secrets-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PerTenantKid_EachTenantDecryptsItsOwnSecret()
    {
        // certsRoot/{kid}/cert.pfx — kid = tenant id (the documented multi-tenant folder layout).
        using var certA = GenerateTenantCert("tenantA");
        using var certB = GenerateTenantCert("tenantB");

        var backends = new Dictionary<string, InMemoryBackend>();
        IStoreBackend BackendFor(string? tenant)
        {
            if (!backends.TryGetValue(tenant ?? "", out var b)) backends[tenant ?? ""] = b = new InMemoryBackend();
            return b;
        }

        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<VaultConfig>().FromStaticJson("{}"),
                rules.For<VaultConfig>().FromStore((a, _) => BackendFor(a.Tenant)).TenantScoped(),
            ])
            .UseSecretsSetup(secrets => secrets.UseCertificatesFromFolder(_certsRoot))
            .UseDebounce(25));

        using var provider = services.BuildServiceProvider();
        var mgr = provider.GetRequiredService<ConfigManager>();
        var tenants = (ITenantConfigurationAccessor)mgr;

        await tenants.InitializeTenantAsync("tenantA");
        await tenants.InitializeTenantAsync("tenantB");

        // Tenant A: encrypt "secret-A" to A's public key (kid=tenantA), write to A's overlay -> A decrypts.
        await mgr.GetWritableStoreForTenant<VaultConfig>("tenantA")
            .SetSecretAsync(x => x.ApiKey!, EncryptForKid(certA.GetRSAPublicKey()!, "tenantA", "secret-A"));
        await TenantWait.UntilAsync(() => mgr.GetConfigForTenant<VaultConfig>("tenantA")?.ApiKey is not null, "tenant A secret applied");
        using (var leaseA = mgr.GetConfigForTenant<VaultConfig>("tenantA")!.ApiKey!.Open())
            Assert.Equal("secret-A", leaseA.Value);

        // Tenant B: its own kid + cert.
        await mgr.GetWritableStoreForTenant<VaultConfig>("tenantB")
            .SetSecretAsync(x => x.ApiKey!, EncryptForKid(certB.GetRSAPublicKey()!, "tenantB", "secret-B"));
        await TenantWait.UntilAsync(() => mgr.GetConfigForTenant<VaultConfig>("tenantB")?.ApiKey is not null, "tenant B secret applied");
        using (var leaseB = mgr.GetConfigForTenant<VaultConfig>("tenantB")!.ApiKey!.Open())
            Assert.Equal("secret-B", leaseB.Value);
    }

    private X509Certificate2 GenerateTenantCert(string kid)
    {
        var kidFolder = Path.Combine(_certsRoot, kid);
        Directory.CreateDirectory(kidFolder);
        var pfxPath = Path.Combine(kidFolder, "cert.pfx");
        return X509CertificateGenerator.GenerateAndSavePfx(pfxPath, password: null, $"CN=Cocoar {kid}", overwrite: true);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_certsRoot)) Directory.Delete(_certsRoot, recursive: true); } catch { /* best effort */ }
    }

    // ---- client-side envelope encryption (what a browser/producer does with only the public key) ----

    private static SecretEnvelope<string> EncryptForKid(RSA rsaPublic, string kid, string value)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value);

        Span<byte> dek = stackalloc byte[32];
        RandomNumberGenerator.Fill(dek);
        try
        {
            var iv = new byte[12];
            RandomNumberGenerator.Fill(iv);
            var ct = new byte[plaintext.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(dek, tag.Length))
            {
                aes.Encrypt(iv, plaintext, ct, tag, associatedData: null);
            }

            var wk = rsaPublic.Encrypt(dek.ToArray(), RSAEncryptionPadding.OaepSHA256);

            return new SecretEnvelope<string>
            {
                Kid = kid,
                Wk = ToBase64Url(wk),
                Iv = ToBase64Url(iv),
                Ct = ToBase64Url(ct),
                Tag = ToBase64Url(tag),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
