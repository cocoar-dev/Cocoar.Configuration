using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.ConfigHub.Tests;

public sealed class ConfigHubRegistrationLifecycleTests
{
    public sealed class Settings { public string Message { get; set; } = ""; }
    public sealed class OtherSettings { public int Port { get; set; } }
    private const string Url = "https://config.example/api/config/app";
    private static HttpResponseMessage Json(string json = "{}", HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Capabilities(bool schema = true) => Json(schema
        ? """{"ProtocolVersion":1,"Features":["schema"]}"""
        : """{"ProtocolVersion":1,"Features":[]}""");
    private static ConfigHubRegistrationOptions Options() => new()
        { RefreshInterval = TimeSpan.FromMilliseconds(40), RequestTimeout = TimeSpan.FromSeconds(2) };

    [Fact]
    public async Task WithoutExtension_DeliveryDoesNotDiscoverOrRegister()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("""{"Message":"remote"}""")));
        await using var manager = ConfigManager.Create(c => c.UseConfiguration(r =>
            [r.For<Settings>().FromConfigHub(new ConfigHubRuleOptions(Url, "tok", handler: handler)).Required()]));
        Assert.Equal("remote", manager.GetConfig<Settings>()!.Message);
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/capabilities") || r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task MixedServers_GroupRules_OnlySendToSupportedTarget_AndIsolateFailures()
    {
        var put = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (r, ct) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
            {
                if (r.RequestUri.AbsolutePath.Contains("broken")) throw new HttpRequestException("unavailable");
                return Capabilities(r.RequestUri.AbsolutePath.Contains("pro"));
            }
            if (r.Method == HttpMethod.Put) put.TrySetResult(await r.Content!.ReadAsStringAsync(ct));
            return Json();
        });
        var pro = new ConfigHubRuleOptions(Url + "-pro", "tok", handler: handler);
        await using var manager = ConfigManager.Create(c => c.UseConfiguration(r => [
            r.For<Settings>().FromConfigHub(new ConfigHubRuleOptions(Url + "-broken", "tok", handler: handler)),
            r.For<Settings>().FromConfigHub(new ConfigHubRuleOptions(Url + "-free", "tok", handler: handler)),
            r.For<Settings>().FromConfigHub(pro), r.For<OtherSettings>().FromConfigHub(pro),
        ]).UseConfigHubRegistration(Options()));
        var body = await put.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("Settings", body);
        Assert.Contains("OtherSettings", body);
        Assert.All(handler.Requests.Where(r => r.Method == HttpMethod.Put), r => Assert.Contains("-pro/schema", r.Path));
        Assert.Equal(1, handler.Requests.Count(r => r.Path.Contains("-pro/capabilities")));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task OlderServer_DisablesUpstreamWithoutBreakingDelivery(HttpStatusCode status)
    {
        var discovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/capabilities")) { discovered.TrySetResult(); return Task.FromResult(Json(status: status)); }
            return Task.FromResult(Json("""{"Message":"remote"}"""));
        });
        await using var manager = ConfigManager.Create(c => c.UseConfiguration(r =>
            [r.For<Settings>().FromConfigHub(new ConfigHubRuleOptions(Url, "tok", handler: handler)).Required()])
            .UseConfigHubRegistration(Options()));
        await discovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.DisposeAsync();
        Assert.Equal(0, handler.Requests.Count(r => r.Method == HttpMethod.Put));
    }

    [Fact]
    public async Task RetryAndRediscovery_DetectChangedEndpoint_AndAvoidDuplicateRegistration()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var url = Url + "-free";
        var puts = 0; var proDiscoveries = 0;
        using var handler = new Handler((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
            {
                if (r.RequestUri.AbsolutePath.Contains("-free")) { first.TrySetResult(); return Task.FromResult(Capabilities(false)); }
                if (Interlocked.Increment(ref proDiscoveries) >= 4) done.TrySetResult();
                return Task.FromResult(Capabilities());
            }
            if (r.Method == HttpMethod.Put && Interlocked.Increment(ref puts) == 1)
                return Task.FromResult(Json(status: HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(Json());
        });
        await using var manager = ConfigManager.Create(c => c.UseConfiguration(r =>
            [r.For<Settings>().FromConfigHub(_ => new ConfigHubRuleOptions(Volatile.Read(ref url), "tok", handler: handler))])
            .UseConfigHubRegistration(Options()));
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Write(ref url, Url + "-pro");
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.DisposeAsync();
        Assert.Equal(2, puts); // one refusal, one successful registration; unchanged reports are suppressed
    }

    [Fact]
    public async Task CapabilityRevocationAndReactivation_RegistersAgain()
    {
        var registered = new SemaphoreSlim(0);
        var disabled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enabled = true;
        using var handler = new Handler((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
            {
                var current = Volatile.Read(ref enabled);
                if (!current) disabled.TrySetResult();
                return Task.FromResult(Capabilities(current));
            }
            if (r.Method == HttpMethod.Put) registered.Release();
            return Task.FromResult(Json());
        });
        await using var manager = ConfigManager.Create(c => c.UseConfiguration(r =>
            [r.For<Settings>().FromConfigHub(new ConfigHubRuleOptions(Url, "tok", handler: handler))])
            .UseConfigHubRegistration(Options()));
        Assert.True(await registered.WaitAsync(TimeSpan.FromSeconds(5)));
        Volatile.Write(ref enabled, false);
        await disabled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Write(ref enabled, true);
        Assert.True(await registered.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ManagerShutdown_CancelsInFlightRequest_AndLeavesCallerTransportOpen()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        using var handler = new Handler(async (r, ct) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { cancelled = true; throw; }
            }
            return Json();
        });
        var manager = ConfigManager.Create(c => c.UseConfiguration(r =>
            [r.For<Settings>().FromConfigHub(new ConfigHubRuleOptions(Url, "tok", handler: handler))])
            .UseConfigHubRegistration());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.DisposeAsync();
        Assert.True(cancelled);
        Assert.False(handler.Disposed);
    }

    [Fact]
    public async Task EncryptionKeyCapability_ReportsOnlyPublicMaterial_WithoutSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"confighub-registration-{Guid.NewGuid():N}.pfx");
        X509CertificateGenerator.GenerateAndSavePfx(path, password: null, "CN=RegistrationTest", validYears: 1, keySize: 2048).Dispose();
        try
        {
            var put = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new Handler(async (r, ct) =>
            {
                if (r.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
                    return Json("""{"ProtocolVersion":1,"Features":["encryption-key"]}""");
                if (r.Method == HttpMethod.Put) put.TrySetResult(await r.Content!.ReadAsStringAsync(ct));
                return Json();
            });
            await using var manager = ConfigManager.Create(c => c.UseConfiguration(r =>
                [r.For<Settings>().FromConfigHub(new ConfigHubRuleOptions(Url, "tok", handler: handler))])
                .UseSecretsSetup(s => s.UseCertificateFromFile(path).WithKeyId("test-instance"))
                .UseConfigHubRegistration());
            var body = System.Text.Json.Nodes.JsonNode.Parse(await put.Task.WaitAsync(TimeSpan.FromSeconds(5)))!.AsObject();
            Assert.Equal("test-instance", (string?)body["kid"]);
            Assert.Equal(manager.GetCurrentEncryptionKey()!.PublicKey, (string?)body["publicKey"]);
            Assert.Equal(["kid", "alg", "walg", "enc", "format", "encoding", "publicKey"], body.Select(p => p.Key));
            Assert.All(handler.Requests.Where(r => r.Method == HttpMethod.Put), r => Assert.EndsWith("/encryption-key", r.Path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BrokenDynamicRule_DoesNotPreventOtherTargetsFromRefreshing()
    {
        var activateBroken = false;
        var discoveries = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
            {
                Volatile.Write(ref activateBroken, true);
                if (Interlocked.Increment(ref discoveries) >= 3) done.TrySetResult();
                return Task.FromResult(Capabilities());
            }
            return Task.FromResult(Json());
        });
        await using var manager = ConfigManager.Create(c => c.UseConfiguration(r => [
            r.For<OtherSettings>().FromConfigHub(_ => throw new InvalidOperationException("unresolved deployment"))
                .When(_ => Volatile.Read(ref activateBroken)),
            r.For<Settings>().FromConfigHub(new ConfigHubRuleOptions(Url, "tok", handler: handler)),
        ]).UseConfigHubRegistration(Options()));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public ConcurrentQueue<(HttpMethod Method, string Path)> Requests { get; } = new();
        public bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue((request.Method, request.RequestUri!.AbsolutePath));
            return send(request, ct);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
