using System.Text.Json;

namespace Cocoar.Configuration.ConfigHub.Tests;

public sealed class ConfigHubOptionsTests
{
    [Theory]
    [InlineData("config.example/api/config/orders")]
    [InlineData("ftp://config.example/api/config/orders")]
    [InlineData("")]
    public void QueryRequiresAbsoluteHttpEndpoint(string url)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ConfigHubProviderQueryOptions(url, "chcfg_valid"));

        Assert.Equal("url", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void QueryRequiresDeliveryToken(string deliveryToken)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ConfigHubProviderQueryOptions(
                "https://config.example/api/config/orders",
                deliveryToken));

        Assert.Equal("deliveryToken", exception.ParamName);
    }

    [Fact]
    public void InvalidBearerTokenIsRejectedWithoutEchoingCredential()
    {
        const string deliveryToken = "chcfg_secret\r\nX-Injected: value";

        var exception = Assert.Throws<ArgumentException>(
            () => new ConfigHubProviderQueryOptions(
                "https://config.example/api/config/orders",
                deliveryToken));

        Assert.DoesNotContain(deliveryToken, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ProviderOptionsRequirePositiveIntervals(int milliseconds)
    {
        var interval = TimeSpan.FromMilliseconds(milliseconds);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConfigHubProviderOptions(fallbackPollInterval: interval));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConfigHubProviderOptions(sseReadIdleTimeout: interval));
    }

    [Fact]
    public void CredentialRotationChangesQueryIdentityWithoutSerializingToken()
    {
        var first = new ConfigHubProviderQueryOptions(
            "https://config.example/api/config/orders",
            "chcfg_first");
        var rotated = new ConfigHubProviderQueryOptions(
            "https://config.example/api/config/orders",
            "chcfg_rotated");

        var firstJson = JsonSerializer.Serialize(first);
        var rotatedJson = JsonSerializer.Serialize(rotated);

        Assert.NotEqual(first.CredentialFingerprint, rotated.CredentialFingerprint);
        Assert.NotEqual(firstJson, rotatedJson);
        Assert.DoesNotContain("chcfg_first", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("chcfg_rotated", rotatedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleOptionsSerializationExcludesSecretsAndHandler()
    {
        using var handler = new HttpClientHandler();
        var options = new ConfigHubRuleOptions(
            "https://config.example/api/config/orders",
            "chcfg_secret",
            handler: handler);

        var json = JsonSerializer.Serialize(options);

        Assert.DoesNotContain("chcfg_secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Handler", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomHandlerDisablesProviderSharingAndRemainsCallerOwned()
    {
        var handler = new TrackingHandler();
        var options = new ConfigHubProviderOptions(handler: handler);

        Assert.Null(options.GenerateProviderKey());

        using (var provider = new ConfigHubProvider(options))
        {
        }

        Assert.False(handler.IsDisposed);
        handler.Dispose();
        Assert.True(handler.IsDisposed);
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public bool IsDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
