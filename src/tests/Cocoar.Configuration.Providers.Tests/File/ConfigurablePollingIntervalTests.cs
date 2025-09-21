using System.Text.Json;
using Cocoar.Configuration.Providers;
using Xunit;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Providers.Tests.File;

/// <summary>
/// Tests for configurable polling interval functionality in FileSourceProvider.
/// Validates that different polling intervals work correctly for testing scenarios.
/// </summary>
public class ConfigurablePollingIntervalTests
{
    private readonly ITestOutputHelper _output;

    public ConfigurablePollingIntervalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    [Trait("Category", "PollingConfiguration")]
    public void FileSourceProviderOptions_CustomPollingInterval_ConfiguresCorrectly()
    {
        // Arrange
        var testInterval = TimeSpan.FromMilliseconds(500);
        
        // Act
        var options = new FileSourceProviderOptions(
            directory: "test-dir",
            debounceTime: TimeSpan.FromMilliseconds(100),
            pollingInterval: testInterval
        );

        // Assert
        Assert.Equal(testInterval, options.PollingInterval);
        _output.WriteLine($"Configured polling interval: {options.PollingInterval.TotalMilliseconds}ms");
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    [Trait("Category", "PollingConfiguration")]
    public void FileSourceProviderOptions_DefaultPollingInterval_IsTenSeconds()
    {
        // Arrange & Act
        var options = new FileSourceProviderOptions(directory: "test-dir");

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), options.PollingInterval);
        _output.WriteLine($"Default polling interval: {options.PollingInterval.TotalSeconds} seconds");
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    [Trait("Category", "PollingConfiguration")]
    public void FileSourceProviderOptions_DifferentPollingIntervals_GenerateDifferentProviderKeys()
    {
        // Arrange
        var options1 = new FileSourceProviderOptions(
            directory: "test-dir",
            pollingInterval: TimeSpan.FromSeconds(1)
        );
        var options2 = new FileSourceProviderOptions(
            directory: "test-dir",
            pollingInterval: TimeSpan.FromSeconds(5)
        );
        var options3 = new FileSourceProviderOptions(
            directory: "test-dir",
            pollingInterval: TimeSpan.FromSeconds(1)  // Same as options1
        );

        // Act
        var key1 = options1.GenerateProviderKey();
        var key2 = options2.GenerateProviderKey();
        var key3 = options3.GenerateProviderKey();

        // Assert
        Assert.NotEqual(key1, key2);  // Different intervals should create different keys
        Assert.Equal(key1, key3);     // Same intervals should create same keys
        
        _output.WriteLine($"Provider key 1 (1s): {key1}");
        _output.WriteLine($"Provider key 2 (5s): {key2}");
        _output.WriteLine($"Provider key 3 (1s): {key3}");
        
        // Keys should include both directory and polling interval
        Assert.Contains("test-dir", key1);
        Assert.Contains("test-dir", key2);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    [Trait("Category", "PollingConfiguration")]
    public void FileSourceRuleOptions_CustomPollingInterval_PassedToProviderOptions()
    {
        // Arrange
        var testInterval = TimeSpan.FromMilliseconds(200);
        
        // Act
        var ruleOptions = new FileSourceRuleOptions(
            directory: "configs",
            filename: "app.json",
            debounceTime: TimeSpan.FromMilliseconds(100),
            pollingInterval: testInterval
        );
        
        var providerOptions = ruleOptions.ToProviderOptions();

        // Assert
        Assert.Equal(testInterval, providerOptions.PollingInterval);
        _output.WriteLine($"Rule options polling interval: {ruleOptions.PollingInterval?.TotalMilliseconds}ms");
        _output.WriteLine($"Provider options polling interval: {providerOptions.PollingInterval.TotalMilliseconds}ms");
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    [Trait("Category", "PollingConfiguration")]
    public void FileSourceRuleOptions_FromFilePath_CustomPollingInterval_ConfiguresCorrectly()
    {
        // Arrange
        var testInterval = TimeSpan.FromMilliseconds(750);
        
        // Act
        var ruleOptions = FileSourceRuleOptions.FromFilePath(
            filePath: "configs/app.json",
            debounceTime: TimeSpan.FromMilliseconds(100),
            pollingInterval: testInterval
        );
        
        var providerOptions = ruleOptions.ToProviderOptions();

        // Assert
        Assert.Equal("configs", ruleOptions.Directory);
        Assert.Equal("app.json", ruleOptions.Filename);
        Assert.Equal(testInterval, ruleOptions.PollingInterval);
        Assert.Equal(testInterval, providerOptions.PollingInterval);
        
        _output.WriteLine($"File path: configs/app.json");
        _output.WriteLine($"Split directory: {ruleOptions.Directory}");
        _output.WriteLine($"Split filename: {ruleOptions.Filename}");
        _output.WriteLine($"Configured polling interval: {providerOptions.PollingInterval.TotalMilliseconds}ms");
    }

    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Provider", "FileSourceProvider")]
    [Trait("Category", "PollingConfiguration")]
    public async Task FastPollingInterval_EnablesQuickTesting()
    {
        // Arrange
        using var tempDir = TempDirectoryHelper.Create();
        var configFile = Path.Combine(tempDir.Path, "test.json");
        
        // Use very fast polling for testing (100ms instead of 10 seconds)
        var options = new FileSourceProviderOptions(
            directory: tempDir.Path,
            pollingInterval: TimeSpan.FromMilliseconds(100)
        );
        var query = new FileSourceProviderQueryOptions("test.json");
        
        using var provider = new FileSourceProvider(options);
        
        _output.WriteLine($"Testing with fast polling interval: {options.PollingInterval.TotalMilliseconds}ms");
        
        // Initial state - file doesn't exist, should throw
        var initialException = await Record.ExceptionAsync(() => provider.FetchConfigurationAsync(query));
        Assert.NotNull(initialException);
        _output.WriteLine("Initial fetch failed as expected (file doesn't exist)");
        
        // Create file and test that it's detected quickly
        var testContent = """{"test": "value", "timestamp": "initial"}""";
        System.IO.File.WriteAllText(configFile, testContent);
        _output.WriteLine("Created config file");
        
        // With 100ms polling, we should detect the file within a reasonable time
        // Wait a bit longer than polling interval to ensure detection
        await Task.Delay(300);
        
        // Now fetch should succeed
        var result = await provider.FetchConfigurationAsync(query);
        Assert.True(result.TryGetProperty("test", out var testProp));
        Assert.Equal("value", testProp.GetString());
        
        _output.WriteLine($"Successfully fetched config: {result}");
        _output.WriteLine("✅ Fast polling interval enables quick testing!");
    }
}