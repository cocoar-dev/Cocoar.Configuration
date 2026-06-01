using Xunit;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.Tests.TestUtilities;

namespace Cocoar.Configuration.Providers.Tests.Environment;

public class EnvironmentProviderUnitTests
{
    private sealed class SimpleValueConfig { public int Value { get; set; } }
    private sealed class AppSettings { public LoggingSettings Logging { get; set; } = new(); public string? Feature_Flag { get; set; } }
    private sealed class LoggingSettings { public string? Level { get; set; } }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void MissingExpectedVariable_DoesNotDegradeRule()
    {
        var prefix = "COCOAR_TEST_APP_" + Guid.NewGuid().ToString("N") + "_"; // unique prefix with no variables
        var rule = EnvironmentVariableProvider.CreateRule<object>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));
        Assert.Equal(Health.HealthStatus.Healthy, manager.HealthStatus); // Fetch succeeded with empty object
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void RequiredVariablePresent_BindsScalar_Healthy()
    {
        var prefix = "MYAPP_";
        var variableName = prefix + "Value";
        using var scope = EnvScope.Set(variableName, "200");

        var rule = EnvironmentVariableProvider.CreateRule<SimpleValueConfig>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<SimpleValueConfig>();
        Assert.NotNull(cfg);
        Assert.Equal(200, cfg!.Value);
        Assert.Equal(Health.HealthStatus.Healthy, manager.HealthStatus);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void NestedMapping_DoubleUnderscore_SplitsIntoHierarchy()
    {
        var prefix = "APP"; // no trailing underscore to exercise trimming logic
        using var s1 = EnvScope.Set(prefix + "__Logging__Level", "Debug");

        var rule = EnvironmentVariableProvider.CreateRule<AppSettings>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<AppSettings>();
        Assert.NotNull(cfg);
        Assert.Equal("Debug", cfg!.Logging.Level);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void SingleUnderscore_IsLiteral_NoHierarchySplit()
    {
        var prefix = "APP";
        using var s1 = EnvScope.Set(prefix + "_Feature_Flag", "enabled");

        var rule = EnvironmentVariableProvider.CreateRule<AppSettings>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<AppSettings>();
        Assert.NotNull(cfg);
        Assert.Equal("enabled", cfg!.Feature_Flag);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void ColonSeparator_AlsoCreatesHierarchy()
    {
        var prefix = "APP_";
        using var s1 = EnvScope.Set(prefix + "Logging:Level", "Info");

        var rule = EnvironmentVariableProvider.CreateRule<AppSettings>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<AppSettings>();
        Assert.NotNull(cfg);
        Assert.Equal("Info", cfg!.Logging.Level);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void TripleUnderscore_StillSeparates()
    {
        var prefix = "APP_";
        using var s1 = EnvScope.Set(prefix + "Logging___Level", "Warn");

        var rule = EnvironmentVariableProvider.CreateRule<AppSettings>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<AppSettings>();
        Assert.NotNull(cfg);
        Assert.Equal("Warn", cfg!.Logging.Level);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void EmptyPrefix_LoadsAllVariables()
    {
        var testVar = "COCOAR_UNIT_TEST_" + Guid.NewGuid().ToString("N");
        using var s1 = EnvScope.Set(testVar, "global-value");

        var rule = EnvironmentVariableProvider.CreateRule<Dictionary<string, object>>(null, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<Dictionary<string, object>>();
        Assert.NotNull(cfg);
        Assert.True(cfg!.ContainsKey(testVar)); // Should include our test variable
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void NonDelimiterPrefixCharacter_Works()
    {
        var prefix = "Marten@";
        var key = prefix + "ConnectionString";
        using var scope = EnvScope.Set(key, "CS");

        var rule = EnvironmentVariableProvider.CreateRule<Dictionary<string, object>>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<Dictionary<string, object>>();
        Assert.NotNull(cfg);
        Assert.True(cfg!.TryGetValue("ConnectionString", out var cs));
        Assert.Equal("CS", cs.ToString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void SingleLeadingUnderscore_IsTrimmed()
    {
        var prefix = "MYAPP";
        var key = prefix + "_FOO"; // will become FOO after trimming the single leading separator
        using var scope = EnvScope.Set(key, "x");

        var rule = EnvironmentVariableProvider.CreateRule<Dictionary<string, object>>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<Dictionary<string, object>>();
        Assert.NotNull(cfg);
        Assert.True(cfg!.TryGetValue("FOO", out var val));
        Assert.Equal("x", val.ToString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "EnvironmentVariableProvider")]
    public void ComplexDelimiterCombinations_AllWork()
    {
        var prefix = "MYAPP";
        // Literal single underscore remains in key part
        using var s1 = EnvScope.Set("MYAPP_FOO_BAR", "x");
        // Double underscore => nesting
        using var s2 = EnvScope.Set("MYAPP__Logging__Level", "Debug");
        // Colon separator => nesting
        using var s3 = EnvScope.Set("MYAPP:Data:ConnectionString", "cs");

        var rule = EnvironmentVariableProvider.CreateRule<Dictionary<string, object>>(prefix, required: true);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(new[]{rule}));

        var cfg = manager.GetConfig<Dictionary<string, object>>();
        Assert.NotNull(cfg);

        // Single underscore remains literal
        Assert.True(cfg!.TryGetValue("FOO_BAR", out var fooBar));
        Assert.Equal("x", fooBar.ToString());

        // Double underscore creates nesting - JsonElement handling
        Assert.True(cfg.TryGetValue("Logging", out var logging));
        if (logging is System.Text.Json.JsonElement loggingJson)
        {
            Assert.True(loggingJson.TryGetProperty("Level", out var lvl));
            Assert.Equal("Debug", lvl.GetString());
        }

        // Colon creates nesting - JsonElement handling
        Assert.True(cfg.TryGetValue("Data", out var data));
        if (data is System.Text.Json.JsonElement dataJson)
        {
            Assert.True(dataJson.TryGetProperty("ConnectionString", out var cs));
            Assert.Equal("cs", cs.GetString());
        }
    }
}
