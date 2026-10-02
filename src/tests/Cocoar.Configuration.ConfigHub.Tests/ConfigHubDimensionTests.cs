using System.Net;
using System.Text;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.ConfigHub.Tests;

/// <summary>
/// Reported dimensions (sent as <c>ConfigHub-Dimension</c>) and ConfigHub's warnings about
/// them (<c>ConfigHub-Warning</c>): logged and passed to a callback by default, or turned
/// into a failed fetch when the application asks for it.
/// </summary>
public sealed class ConfigHubDimensionTests
{
    private const string Url = "https://config.example/api/config/shop-acme-dev";

    [Fact]
    public async Task ReportedDimensions_AreSentAsHeader()
    {
        using var handler = new RecordingHandler();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: handler));
        var query = new ConfigHubProviderQueryOptions(Url, "chcfg_secret",
            new Dictionary<string, string> { ["server"] = "APPDEV01", ["region"] = "eu west" });

        await provider.FetchConfigurationBytesAsync(query, CancellationToken.None);

        // Sorted by key, values escaped so commas or '=' in a value cannot break the list.
        Assert.Equal("region=eu%20west, server=APPDEV01", handler.DimensionHeader);
    }

    [Fact]
    public async Task WithoutDimensions_NoHeaderIsSent()
    {
        using var handler = new RecordingHandler();
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: handler));

        await provider.FetchConfigurationBytesAsync(new ConfigHubProviderQueryOptions(Url, "chcfg_secret"), CancellationToken.None);

        Assert.Null(handler.DimensionHeader);
    }

    [Fact]
    public void Dimensions_ArePartOfTheQueryIdentity_ButNotTheToken()
    {
        var a = new ConfigHubProviderQueryOptions(Url, "chcfg_secret", new Dictionary<string, string> { ["server"] = "A" });
        var b = new ConfigHubProviderQueryOptions(Url, "chcfg_secret", new Dictionary<string, string> { ["server"] = "B" });

        var jsonA = System.Text.Json.JsonSerializer.Serialize(a);
        var jsonB = System.Text.Json.JsonSerializer.Serialize(b);

        Assert.NotEqual(jsonA, jsonB);
        Assert.DoesNotContain("chcfg_secret", jsonA, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidDimensionKey_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ConfigHubProviderQueryOptions(Url, "chcfg_secret",
            new Dictionary<string, string> { ["server name"] = "x" }));
    }

    [Fact]
    public async Task Warnings_DefaultToWarn_AndReachTheCallbackOncePerChange()
    {
        using var handler = new RecordingHandler { Warning = "missing=server; unknown=region:mars" };
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: handler));
        var seen = new List<IReadOnlyList<string>>();
        var query = new ConfigHubProviderQueryOptions(Url, "chcfg_secret", onWarnings: seen.Add);

        var bytes = await provider.FetchConfigurationBytesAsync(query, CancellationToken.None);
        await provider.FetchConfigurationBytesAsync(query, CancellationToken.None);

        Assert.Equal("{\"Value\":\"remote\"}", Encoding.UTF8.GetString(bytes));
        var warnings = Assert.Single(seen);
        Assert.Equal(["missing=server", "unknown=region:mars"], warnings);
    }

    [Fact]
    public async Task FailMode_TurnsWarningsIntoAFailedFetch()
    {
        using var handler = new RecordingHandler { Warning = "missing=server" };
        using var provider = new ConfigHubProvider(new ConfigHubProviderOptions(handler: handler));
        var query = new ConfigHubProviderQueryOptions(Url, "chcfg_secret", warningMode: ConfigHubWarningMode.Fail);

        var exception = await Assert.ThrowsAsync<ConfigHubWarningException>(
            () => provider.FetchConfigurationBytesAsync(query, CancellationToken.None));

        Assert.Equal(["missing=server"], exception.Warnings);
    }

    [Fact]
    public void FailMode_WithRequiredRule_FailsStartup()
    {
        using var handler = new RecordingHandler { Warning = "missing=server" };

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var manager = ConfigManager.Create(configuration => configuration
                .UseConfiguration(rules =>
                [
                    rules.For<RemoteSettings>().FromConfigHub(
                            new ConfigHubRuleOptions(Url, "chcfg_secret", handler: handler)
                                .WithWarnings(ConfigHubWarningMode.Fail))
                        .Required(),
                ]));
        });

        Assert.Contains("missing=server", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WarnMode_WithRequiredRule_StartsWithTheDeliveredConfiguration()
    {
        using var handler = new RecordingHandler { Warning = "missing=server" };

        using var manager = ConfigManager.Create(configuration => configuration
            .UseConfiguration(rules =>
            [
                rules.For<RemoteSettings>().FromConfigHub(
                        new ConfigHubRuleOptions(Url, "chcfg_secret", handler: handler)
                            .WithDimension("other", "x"))
                    .Required(),
            ]));

        Assert.Equal("remote", manager.GetConfig<RemoteSettings>()!.Value);
        Assert.Equal("other=x", handler.DimensionHeader);
    }

    [Fact]
    public void WithDimension_EmptyValue_RemovesTheDimension()
    {
        var options = new ConfigHubRuleOptions(Url, "chcfg_secret")
            .WithDimension("server", "APPDEV01")
            .WithDimension("server", "");

        Assert.Empty(options.Dimensions);
    }

    private sealed class RemoteSettings
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Warning { get; init; }
        public string? DimensionHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            DimensionHeader = request.Headers.TryGetValues(ConfigHubProvider.DimensionHeader, out var values)
                ? string.Join(", ", values)
                : null;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Value\":\"remote\"}", Encoding.UTF8, "application/json"),
            };
            if (Warning is not null)
                response.Headers.TryAddWithoutValidation(ConfigHubProvider.WarningHeader, Warning);
            return Task.FromResult(response);
        }
    }
}
