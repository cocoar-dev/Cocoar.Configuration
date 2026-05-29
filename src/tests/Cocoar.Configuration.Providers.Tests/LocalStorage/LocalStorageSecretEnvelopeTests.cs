using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.LocalStorage;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;
using Cocoar.Configuration.X509Encryption;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.LocalStorage;

[Trait("Type", "Unit")]
public sealed class LocalStorageSecretEnvelopeTests
{
    public sealed class VaultConfig
    {
        public Secret<string>? ApiKey { get; set; }
    }

    [Fact]
    public async Task SetSecretAsync_StoresEncryptedEnvelope_DecryptsToOriginalValue()
    {
        const string kid = "test-kid";
        var pfxPath = Path.Combine(Path.GetTempPath(), "cocoar_secret_" + Guid.NewGuid().ToString("N") + ".pfx");
        using var cert = X509CertificateGenerator.GenerateAndSavePfx(pfxPath, password: null, "CN=Cocoar Test", overwrite: true);
        try
        {
            // Encrypt CLIENT-SIDE (here: with the cert) — the server only ever receives the envelope.
            var envelope = BuildEnvelope(cert, kid, "super-secret-value");
            var backend = new InMemoryBackend();

            var services = new ServiceCollection();
            services.AddCocoarConfiguration(c => c
                .UseConfiguration(rules => new ConfigRule[]
                {
                    rules.For<VaultConfig>().FromStaticJson("{}"),
                    rules.For<VaultConfig>().FromLocalStorage(backend),
                })
                .UseSecretsSetup(secrets => secrets.UseCertificateFromFile(pfxPath).WithKeyId(kid)));

            using var provider = services.BuildServiceProvider();
            var storage = provider.GetRequiredService<ILocalStorage<VaultConfig>>();
            var manager = provider.GetRequiredService<ConfigManager>();

            await storage.SetSecretAsync(x => x.ApiKey!, envelope);

            await ActiveWaitHelpers.WaitUntilAsync(
                () => manager.GetConfig<VaultConfig>()?.ApiKey is not null,
                TimeSpan.FromSeconds(5), description: "encrypted secret override applied");

            var config = manager.GetConfig<VaultConfig>()!;
            using var lease = config.ApiKey!.Open();
            Assert.Equal("super-secret-value", lease.Value);
        }
        finally
        {
            if (System.IO.File.Exists(pfxPath)) System.IO.File.Delete(pfxPath);
        }
    }

    [Fact]
    public async Task SetSecretEnvelopeAsync_RejectsPlaintext()
    {
        using var provider = BuildMinimalProvider();
        var overlay = provider.GetRequiredService<ILocalStorageOverlay<VaultConfig>>();

        // A bare string is not a cocoar.secret envelope → rejected before anything is stored.
        await Assert.ThrowsAsync<ArgumentException>(
            () => overlay.SetSecretEnvelopeAsync("ApiKey", JsonValue.Create("plaintext-leak")));
    }

    [Fact]
    public void SetAsync_OnSecretMember_StillThrowsNotSupported()
    {
        using var provider = BuildMinimalProvider();
        var storage = provider.GetRequiredService<ILocalStorage<VaultConfig>>();

        // The normal typed SetAsync must keep rejecting secret members (no plaintext into the overlay).
        Assert.Throws<NotSupportedException>(
            () => { _ = storage.SetAsync(x => x.ApiKey!, Secret<string>.FromPlain("x")); });
    }

    private static ServiceProvider BuildMinimalProvider()
    {
        var backend = new InMemoryBackend();
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules => new ConfigRule[]
        {
            rules.For<VaultConfig>().FromStaticJson("{}"),
            rules.For<VaultConfig>().FromLocalStorage(backend),
        }));
        return services.BuildServiceProvider();
    }

    private static JsonObject BuildEnvelope(X509Certificate2 cert, string kid, string value)
    {
        var hybrid = new X509HybridCrypto(cert).Encrypt(JsonSerializer.Serialize(value));
        // The runtime decrypt path (HybridEnvelope byte[] fields) reads base64url WITHOUT padding — this is
        // exactly what a browser must emit. (X509HybridCrypto produces standard base64, so we convert.)
        return new JsonObject
        {
            ["type"] = "cocoar.secret",
            ["version"] = 1,
            ["kid"] = kid,
            ["alg"] = "RSA-OAEP-AES256-GCM",
            ["wk"] = ToBase64Url(hybrid.WrappedKey),
            ["walg"] = hybrid.WrappingAlgorithm,
            ["iv"] = ToBase64Url(hybrid.Iv),
            ["ct"] = ToBase64Url(hybrid.Ciphertext),
            ["tag"] = ToBase64Url(hybrid.Tag),
        };
    }

    private static string ToBase64Url(string standardBase64)
        => standardBase64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
