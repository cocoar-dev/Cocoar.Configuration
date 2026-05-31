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

/// <summary>
/// (Secrets publishing) per-tenant encryption-key publishing on the folder (kid = tenant) model:
/// each tenant publishes exactly ONE current public key (the newest cert in its subfolder) and the
/// provider never exposes another tenant's key. The published key round-trips end to end: a producer
/// encrypts with it, the value is written to THAT tenant's WritableStore overlay, and the tenant
/// decrypts the original — closing the loop that motivated multi-tenancy.
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public sealed class TenantSecretEncryptionKeyTests : IDisposable
{
    private readonly string _certsRoot = Path.Combine(Path.GetTempPath(), "cocoar-mt-keypub-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetCurrentKeyForTenant_PublishesPerTenantKey_NoCrossTenantExposure()
    {
        using var certA = GenerateTenantCert("tenantA");
        using var certB = GenerateTenantCert("tenantB");

        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseSecretsSetup(secrets => secrets.UseCertificatesFromFolder(_certsRoot)));
        using var provider = services.BuildServiceProvider();

        var keyProvider = provider.GetRequiredService<ISecretEncryptionKeyProvider>();

        var keyA = keyProvider.GetCurrentKeyForTenant("tenantA");
        var keyB = keyProvider.GetCurrentKeyForTenant("tenantB");

        Assert.NotNull(keyA);
        Assert.NotNull(keyB);
        Assert.Equal("tenantA", keyA!.Kid);
        Assert.Equal("tenantB", keyB!.Kid);

        // Each tenant's published key is exactly its OWN cert's SPKI — never the other's.
        Assert.Equal(ExpectedSpki(certA), keyA.PublicKey);
        Assert.Equal(ExpectedSpki(certB), keyB.PublicKey);
        Assert.NotEqual(keyA.PublicKey, keyB.PublicKey);

        // No publishable key for an unknown tenant, and the single-tenant accessor yields nothing
        // in folder/multi-tenant mode (callers MUST ask per tenant — no list, no leak).
        Assert.Null(keyProvider.GetCurrentKeyForTenant("tenantC"));
        Assert.Null(keyProvider.GetCurrentKey());
    }

    [Fact]
    public async Task PublishedTenantKey_EncryptsValue_WrittenToTenantStore_Decrypts()
    {
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

        // Producer fetches ONLY tenant A's published public key — never the private cert.
        var published = provider.GetRequiredService<ISecretEncryptionKeyProvider>().GetCurrentKeyForTenant("tenantA");
        Assert.NotNull(published);
        Assert.Equal("tenantA", published!.Kid);

        // Encrypt client-side with that public key alone, then write into tenant A's overlay.
        using var rsaPublic = RSA.Create();
        rsaPublic.ImportSubjectPublicKeyInfo(FromBase64Url(published.PublicKey), out _);
        await mgr.GetWritableStoreForTenant<VaultConfig>("tenantA")
            .SetSecretAsync(x => x.ApiKey!, EncryptForKid(rsaPublic, "tenantA", "secret-A"));

        await TenantWait.UntilAsync(
            () => mgr.GetConfigForTenant<VaultConfig>("tenantA")?.ApiKey is not null, "tenant A secret applied");

        using var lease = mgr.GetConfigForTenant<VaultConfig>("tenantA")!.ApiKey!.Open();
        Assert.Equal("secret-A", lease.Value);
    }

    private X509Certificate2 GenerateTenantCert(string kid)
    {
        var kidFolder = Path.Combine(_certsRoot, kid);
        Directory.CreateDirectory(kidFolder);
        var pfxPath = Path.Combine(kidFolder, "cert.pfx");
        return X509CertificateGenerator.GenerateAndSavePfx(pfxPath, password: null, $"CN=Cocoar {kid}", overwrite: true);
    }

    private static string ExpectedSpki(X509Certificate2 cert)
    {
        using var rsa = cert.GetRSAPublicKey()!;
        return ToBase64Url(rsa.ExportSubjectPublicKeyInfo());
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

    private static byte[] FromBase64Url(string value)
    {
        var b64 = value.Replace('-', '+').Replace('_', '/');
        b64 += (b64.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(b64);
    }
}
