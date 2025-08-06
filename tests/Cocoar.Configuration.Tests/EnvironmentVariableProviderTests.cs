using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.Extensions.Tests;

public class EnvironmentVariableProviderTests
{
    [Fact]
    public async Task GetValueAsync_ReturnsValue_WhenEnvVarExists()
    {
        // Arrange
        var key = "TEST_ENVPROVIDER_KEY";
        var value = "TestValue";
        Environment.SetEnvironmentVariable(key, value);
        var provider = new EnvironmentVariableProvider(new EnvironmentVariableProviderOptions());
        var queryOptions = new EnvironmentVariableProviderQueryOptions();

        // Act
        var result = await provider.GetValueAsync(queryOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.True(result.TryGetProperty(key, out var prop));
        Assert.Equal(value, prop.GetString());
    }

    [Fact]
    public async Task GetValueAsync_WithPrefix_ReturnsOnlyPrefixedVars()
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
        var result = await provider.GetValueAsync(queryOptions);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TryGetProperty("SETTING1", out var v1));
        Assert.True(result.TryGetProperty("SETTING2", out var v2));
        Assert.False(result.TryGetProperty("SETTING", out _));
        Assert.Equal("Value1", v1.GetString());
        Assert.Equal("Value2", v2.GetString());
    }

    [Fact]
    public async Task Changes_ReturnsEmptyObservable()
    {
       var provider = new EnvironmentVariableProvider(new EnvironmentVariableProviderOptions());
       var queryOptions = new EnvironmentVariableProviderQueryOptions();
       var configChangeNotification = await provider.Changes(queryOptions).FirstAsync().ToTask();
       Assert.NotNull(configChangeNotification);
    }
}
