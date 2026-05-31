using System.Security.Cryptography;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.WritableStore;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Tests.WritableStore;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;
using Cocoar.Configuration.X509Encryption;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests;

/// <summary>
/// Verifies the secrets encryption-key publishing accessor: it exposes the configured single kid's
/// current PUBLIC key (SPKI, base64url-no-padding), and a producer holding only that public key can
/// build an envelope the server decrypts — without ever touching the private cert.
/// </summary>
[Trait("Type", "Unit")]
public sealed class SecretEncryptionKeyProviderTests
{
    public sealed class VaultConfig
    {
        public Secret<string>? ApiKey { get; set; }
    }

    [Fact]
    public void GetCurrentKey_SingleKid_PublishesOnePublicKey()
    {
        const string kid = "publish-kid";
        RunWithCert(kid, provider =>
        {
            var keyProvider = provider.GetRequiredService<ISecretEncryptionKeyProvider>();

            var key = keyProvider.GetCurrentKey();
            Assert.NotNull(key);

            Assert.Equal(kid, key!.Kid);
            Assert.Equal(SecretAlgorithms.Hybrid, key.Alg);
            Assert.Equal(SecretAlgorithms.KeyWrap, key.Walg);
            Assert.Equal(SecretAlgorithms.DataEncryption, key.Enc);
            Assert.Equal("spki", key.Format);
            Assert.Equal("base64url", key.Encoding);
            Assert.False(string.IsNullOrEmpty(key.PublicKey));

            // base64url WITHOUT padding — never standard-base64 characters.
            Assert.DoesNotContain('+', key.PublicKey);
            Assert.DoesNotContain('/', key.PublicKey);
            Assert.DoesNotContain('=', key.PublicKey);

            // The published bytes are a valid SubjectPublicKeyInfo.
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(FromBase64Url(key.PublicKey), out _);
        });
    }

    [Fact]
    public void GetCurrentKeyForTenant_ReturnsKeyForConfiguredKid_NullOtherwise()
    {
        const string kid = "lookup-kid";
        RunWithCert(kid, provider =>
        {
            var keyProvider = provider.GetRequiredService<ISecretEncryptionKeyProvider>();

            // Single-tenant accessor publishes the one configured key.
            Assert.NotNull(keyProvider.GetCurrentKey());

            // The same key is reachable by its kid (single-kid mode treats kid == tenant id).
            Assert.NotNull(keyProvider.GetCurrentKeyForTenant(kid));

            // Anything else returns nothing — never a list, never another key.
            Assert.Null(keyProvider.GetCurrentKeyForTenant("not-configured"));
            Assert.Null(keyProvider.GetCurrentKeyForTenant(""));
        });
    }

    [Fact]
    public void NoSecretsConfigured_ProviderNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(_ => Array.Empty<ConfigRule>()));
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<ISecretEncryptionKeyProvider>());
    }

    [Fact]
    public async Task PublishedPublicKey_EncryptsValue_ServerDecryptsToOriginal()
    {
        const string kid = "roundtrip-kid";
        const string secretValue = "round-trip-secret";

        var pfxPath = NewPfxPath();
        using var cert = X509CertificateGenerator.GenerateAndSavePfx(pfxPath, password: null, "CN=Cocoar Test", overwrite: true);
        try
        {
            var backend = new InMemoryBackend();
            var services = new ServiceCollection();
            services.AddCocoarConfiguration(c => c
                .UseConfiguration(rules => new ConfigRule[]
                {
                    rules.For<VaultConfig>().FromStaticJson("{}"),
                    rules.For<VaultConfig>().FromStore(backend),
                })
                .UseSecretsSetup(secrets => secrets.UseCertificateFromFile(pfxPath).WithKeyId(kid)));

            using var provider = services.BuildServiceProvider();

            // 1. Fetch ONLY the published public key — the producer never sees the private cert.
            var published = provider.GetRequiredService<ISecretEncryptionKeyProvider>().GetCurrentKey();
            Assert.NotNull(published);

            // 2. Encrypt client-side with that public key alone (what a browser does).
            using var rsaPublic = RSA.Create();
            rsaPublic.ImportSubjectPublicKeyInfo(FromBase64Url(published!.PublicKey), out _);
            var envelope = EncryptWithPublicKey(rsaPublic, kid, secretValue);

            // 3. Server stores the envelope and decrypts with the matching private key.
            var storage = provider.GetRequiredService<IWritableStore<VaultConfig>>();
            var manager = provider.GetRequiredService<ConfigManager>();
            await storage.SetSecretAsync(x => x.ApiKey!, envelope);

            await ActiveWaitHelpers.WaitUntilAsync(
                () => manager.GetConfig<VaultConfig>()?.ApiKey is not null,
                TimeSpan.FromSeconds(5), description: "published-key envelope applied");

            using var lease = manager.GetConfig<VaultConfig>()!.ApiKey!.Open();
            Assert.Equal(secretValue, lease.Value);
        }
        finally
        {
            if (System.IO.File.Exists(pfxPath)) System.IO.File.Delete(pfxPath);
        }
    }

    private static void RunWithCert(string kid, Action<ServiceProvider> test)
    {
        var pfxPath = NewPfxPath();
        using var cert = X509CertificateGenerator.GenerateAndSavePfx(pfxPath, password: null, "CN=Cocoar Test", overwrite: true);
        try
        {
            var services = new ServiceCollection();
            services.AddCocoarConfiguration(c => c
                .UseConfiguration(_ => Array.Empty<ConfigRule>())
                .UseSecretsSetup(secrets => secrets.UseCertificateFromFile(pfxPath).WithKeyId(kid)));

            using var provider = services.BuildServiceProvider();
            test(provider);
        }
        finally
        {
            if (System.IO.File.Exists(pfxPath)) System.IO.File.Delete(pfxPath);
        }
    }

    private static string NewPfxPath()
        => Path.Combine(Path.GetTempPath(), "cocoar_pubkey_" + Guid.NewGuid().ToString("N") + ".pfx");

    private static SecretEnvelope<string> EncryptWithPublicKey(RSA rsaPublic, string kid, string value)
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
