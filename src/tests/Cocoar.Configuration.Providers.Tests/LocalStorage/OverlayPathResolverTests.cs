using Cocoar.Configuration.Providers;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.LocalStorage;

public class OverlayPathResolverTests
{
    [Fact]
    [Trait("Type", "Unit")]
    public void ResolveKeyPath_SimpleMemberChain()
    {
        var path = OverlayPathResolver.ResolveKeyPath<SmtpSettings, int>(x => x.Nested.Count);
        Assert.Equal("Nested.Count", path);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ResolveKeyPath_TopLevelMember()
    {
        var path = OverlayPathResolver.ResolveKeyPath<SmtpSettings, int>(x => x.Port);
        Assert.Equal("Port", path);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ResolveKeyPath_HonorsJsonPropertyName()
    {
        var path = OverlayPathResolver.ResolveKeyPath<AttributedSettings, string?>(x => x.Renamed);
        Assert.Equal("custom_name", path);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ResolveKeyPath_MethodCall_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            OverlayPathResolver.ResolveKeyPath<SmtpSettings, string?>(x => x.Host!.ToUpperInvariant()));
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ResolveKeyPath_Indexer_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            OverlayPathResolver.ResolveKeyPath<IndexableSettings, string>(x => x.Items[0]));
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void ResolveKeyPath_SecretMember_Throws()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            OverlayPathResolver.ResolveKeyPath<SecretSettings, object?>(x => x.ApiKey));
        Assert.Contains("secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
