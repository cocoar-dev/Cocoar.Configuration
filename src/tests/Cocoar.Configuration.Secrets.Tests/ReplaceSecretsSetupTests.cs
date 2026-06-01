using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;
using Cocoar.Configuration.Testing;

namespace Cocoar.Configuration.Secrets.Tests;

/// <summary>
/// Tests for ReplaceSecretsSetup — the per-concern secrets override that is independent of
/// ReplaceConfiguration / AppendConfiguration.
/// </summary>
public class ReplaceSecretsSetupTests
{
    public ReplaceSecretsSetupTests()
    {
        CocoarTestConfiguration.Clear();
    }

    // Helper: activate a secrets-only override via Apply (ReplaceSecretsSetup is a Secrets extension
    // on TestOverrideBuilder; CocoarTestConfiguration.ReplaceSecretsSetup takes Delegate and can't
    // infer lambda types, so we use the builder pattern for standalone secrets overrides).
    private static TestConfigurationScope ApplySecretsOverride(Func<SecretsBuilder, SetupDefinition> configure)
        => CocoarTestConfiguration.Apply(
            new TestOverrideBuilder().ReplaceSecretsSetup(configure).Build());

    private record AppConfig
    {
        public Secret<string>? ApiKey { get; init; }
        public string Name { get; init; } = "";
    }

    // ------------------------------------------------------------------
    // Standalone secrets override (no rule replacement)
    // ------------------------------------------------------------------

    [Fact]
    public void ReplaceSecretsSetup_AllowsPlaintext_WhenAppCallsUseSecretsSetup()
    {
        using var _ = ApplySecretsOverride(secrets => secrets.AllowPlaintext());

        var manager = ConfigManager.Create(c => c
            .UseConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"test","ApiKey":"plain-value"}""")
            ])
            .UseSecretsSetup(secrets => secrets.AllowPlaintext())); // intercepted by test override

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        using var lease = config.ApiKey!.Open();
        Assert.Equal("plain-value", lease.Value);
    }

    [Fact]
    public void ReplaceSecretsSetup_Alone_DoesNotAffectRules()
    {
        // Only secrets setup is overridden; original rules still run
        using var _ = ApplySecretsOverride(secrets => secrets.AllowPlaintext());

        var manager = ConfigManager.Create(c => c
            .UseConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"from-original","ApiKey":"original-key"}""")
            ])
            .UseSecretsSetup(secrets => secrets.AllowPlaintext()));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        // Original rule still runs — name comes from the original static JSON
        Assert.Equal("from-original", config.Name);
        using var lease = config.ApiKey!.Open();
        Assert.Equal("original-key", lease.Value);
    }

    [Fact]
    public void ReplaceSecretsSetup_DoesNotActivateRulesOverride()
    {
        using var _ = ApplySecretsOverride(secrets => secrets.AllowPlaintext());

        // Context is active but ConfigurationMode is null — rules override is NOT set
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.Null(CocoarTestConfiguration.Current?.ConfigurationMode);
    }

    // ------------------------------------------------------------------
    // Chaining: ReplaceConfiguration + ReplaceSecretsSetup
    // ------------------------------------------------------------------

    [Fact]
    public void ReplaceConfiguration_ChainedWithReplaceSecretsSetup_BothApply()
    {
        using var _ = CocoarTestConfiguration
            .ReplaceConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"overridden","ApiKey":"chained-key"}""")
            ])
            .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext());

        var manager = ConfigManager.Create(c => c
            .UseConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"original","ApiKey":"original-key"}""")
            ])
            .UseSecretsSetup(secrets => secrets.AllowPlaintext()));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Equal("overridden", config.Name);
        using var lease = config.ApiKey!.Open();
        Assert.Equal("chained-key", lease.Value);
    }

    [Fact]
    public void AppendConfiguration_ChainedWithReplaceSecretsSetup_BothApply()
    {
        using var _ = CocoarTestConfiguration
            .AppendConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"appended"}""")
            ])
            .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext());

        var manager = ConfigManager.Create(c => c
            .UseConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"original","ApiKey":"base-key"}""")
            ])
            .UseSecretsSetup(secrets => secrets.AllowPlaintext()));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        // Last rule wins for Name
        Assert.Equal("appended", config.Name);
        // ApiKey from base rule
        using var lease = config.ApiKey!.Open();
        Assert.Equal("base-key", lease.Value);
    }

    // ------------------------------------------------------------------
    // Fixture pattern: TestOverrideBuilder (no auto-activate)
    // ------------------------------------------------------------------

    [Fact]
    public void TestOverrideBuilder_WithReplaceSecretsSetup_BuildsThenApply()
    {
        var context = new TestOverrideBuilder()
            .ReplaceConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"fixture","ApiKey":"fixture-key"}""")
            ])
            .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext())
            .Build();

        // Not yet active
        Assert.False(CocoarTestConfiguration.IsActive);

        using var scope = CocoarTestConfiguration.Apply(context);
        Assert.True(CocoarTestConfiguration.IsActive);

        var manager = ConfigManager.Create(c => c
            .UseConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"original","ApiKey":"original-key"}""")
            ])
            .UseSecretsSetup(secrets => secrets.AllowPlaintext()));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Equal("fixture", config.Name);
        using var lease = config.ApiKey!.Open();
        Assert.Equal("fixture-key", lease.Value);
    }

    // ------------------------------------------------------------------
    // Scope / dispose behavior
    // ------------------------------------------------------------------

    [Fact]
    public void ReplaceSecretsSetup_ClearsOnDispose()
    {
        Assert.False(CocoarTestConfiguration.IsActive);

        using (var scope = ApplySecretsOverride(secrets => secrets.AllowPlaintext()))
        {
            Assert.True(CocoarTestConfiguration.IsActive);
        }

        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void ChainedReplaceSecretsSetup_ClearsOnDispose()
    {
        Assert.False(CocoarTestConfiguration.IsActive);

        using (CocoarTestConfiguration
            .ReplaceConfiguration(rule => [
                rule.For<AppConfig>().FromStaticJson("""{"Name":"test"}""")
            ])
            .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext()))
        {
            Assert.True(CocoarTestConfiguration.IsActive);
        }

        Assert.False(CocoarTestConfiguration.IsActive);
    }
}
