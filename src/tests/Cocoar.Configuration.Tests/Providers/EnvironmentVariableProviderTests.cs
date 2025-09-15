using System.Text.Json;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;

namespace Cocoar.Configuration.Tests;

public class EnvironmentVariableProviderTests
{
    [Fact]
    public async Task FetchConfigurationAsync_ReturnsValue_WhenEnvVarExists()
    {
        // Arrange
        var key = "TESTENVPROVIDERKEY";
        var value = "TestValue";
        Environment.SetEnvironmentVariable(key, value);
        var provider = new EnvironmentVariableProvider(new EnvironmentVariableProviderOptions());
        var queryOptions = new EnvironmentVariableProviderQueryOptions();

        // Act
        var result = await provider.FetchConfigurationAsync(queryOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.True(result.TryGetProperty(key, out var prop));
        Assert.Equal(value, prop.GetString());
    }

    [Fact]
    public async Task FetchConfigurationAsync_Prefix_With_NonDelimiter_Char_Works()
    {
        // Arrange
        var prefix = "Marten@";
        var key = prefix + "ConnectionString";
        Environment.SetEnvironmentVariable(key, "CS");
        var provider = new EnvironmentVariableProvider(new EnvironmentVariableProviderOptions(prefix));
        var queryOptions = new EnvironmentVariableProviderQueryOptions(prefix);

        try
        {
            // Act
            var result = await provider.FetchConfigurationAsync(queryOptions);

            // Assert
            Assert.True(result.TryGetProperty("ConnectionString", out var cs));
            Assert.Equal("CS", cs.GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public async Task FetchConfigurationAsync_Prefix_SingleLeadingUnderscore_IsTrimmed()
    {
        // Arrange
        var prefix = "MYAPP";
        var key = prefix + "_FOO"; // will become FOO after trimming the single leading separator
        Environment.SetEnvironmentVariable(key, "x");
        var provider = new EnvironmentVariableProvider(new EnvironmentVariableProviderOptions(prefix));
        var queryOptions = new EnvironmentVariableProviderQueryOptions(prefix);

        try
        {
            // Act
            var result = await provider.FetchConfigurationAsync(queryOptions);

            // Assert
            Assert.True(result.TryGetProperty("FOO", out var val));
            Assert.Equal("x", val.GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public async Task FetchConfigurationAsync_Uses_DoubleUnderscore_And_Colon_As_Separators()
    {
        // Arrange
        var prefix = "MYAPP";
        // Literal single underscore remains in key part
        Environment.SetEnvironmentVariable("MYAPP_FOO_BAR", "x");
        // Double underscore ⇒ nesting
        Environment.SetEnvironmentVariable("MYAPP__Logging__Level", "Debug");
        // Colon separator ⇒ nesting
        Environment.SetEnvironmentVariable("MYAPP:Data:ConnectionString", "cs");

        var provider = new EnvironmentVariableProvider(new EnvironmentVariableProviderOptions(prefix));
        var queryOptions = new EnvironmentVariableProviderQueryOptions(prefix);

        // Act
        var result = await provider.FetchConfigurationAsync(queryOptions);

        // Assert
        Assert.True(result.TryGetProperty("FOO_BAR", out var fooBar));
        Assert.Equal("x", fooBar.GetString());

        // Ensure nested keys via double underscore are present
        Assert.True(result.TryGetProperty("Logging", out var logging));
        Assert.True(logging.TryGetProperty("Level", out var lvl));
        Assert.True(lvl.ValueKind == JsonValueKind.String);

        Assert.True(result.TryGetProperty("Data", out var data));
        Assert.True(data.TryGetProperty("ConnectionString", out var cs));
        Assert.Equal("cs", cs.GetString());
    }

    [Fact]
    public async Task FetchConfigurationAsync_WithPrefix_ReturnsOnlyPrefixedVars()
    {
        // Arrange
        var prefix = "MYAPP";
        var key1 = "MYAPP_SETTING1";
        var key2 = "MYAPP_SETTING2";
        var keyOther = "OTHERAPP_SETTING";
        Environment.SetEnvironmentVariable(key1, "Value1");
        Environment.SetEnvironmentVariable(key2, "Value2");
        Environment.SetEnvironmentVariable(keyOther, "OtherValue");
        var provider = new EnvironmentVariableProvider(new EnvironmentVariableProviderOptions(prefix));
        var queryOptions = new EnvironmentVariableProviderQueryOptions(prefix);

        // Act
        var result = await provider.FetchConfigurationAsync(queryOptions);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TryGetProperty("SETTING1", out var v1));
        Assert.True(result.TryGetProperty("SETTING2", out var v2));
        Assert.False(result.TryGetProperty("SETTING", out _));
        Assert.Equal("Value1", v1.GetString());
        Assert.Equal("Value2", v2.GetString());
    }

    [Fact]
    public void Changes_DoesNotEmit_OnSubscribe()
    {
        var provider = new EnvironmentVariableProvider(new EnvironmentVariableProviderOptions());
        var queryOptions = new EnvironmentVariableProviderQueryOptions();
        var emitted = false;
        using var sub = provider.Changes(queryOptions).Subscribe(_ => emitted = true);
        // No emission expected immediately
        Assert.False(emitted);
    }
}
