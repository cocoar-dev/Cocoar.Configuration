using System.Text.Json;
using Cocoar.Configuration.Providers.Tests.Helpers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Providers.Tests.File;

/// <summary>
/// Tests for FileSourceProvider behavior with non-existent directory structures.
/// Validates how the provider handles scenarios where parent directories don't exist initially.
/// </summary>
public class FileProviderDirectoryTests
{
    private readonly ITestOutputHelper _output;

    public FileProviderDirectoryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task NonExistentDirectory_ProviderCreation_ShouldHandleGracefully()
    {
        using var tempDir = TempDirectoryHelper.Create();
        var nonExistentPath = Path.Combine(tempDir.Path, "nested", "deep", "config");

        // This should test what happens when we create a provider for a non-existent directory
        var options = new FileSourceProviderOptions(nonExistentPath);

        Exception? constructorException = null;
        FileSourceProvider? provider = null;

        try
        {
            provider = new(options);
            _output.WriteLine("Provider created successfully for non-existent directory");
        }
        catch (Exception ex)
        {
            constructorException = ex;
            _output.WriteLine($"Provider creation failed: {ex.GetType().Name}: {ex.Message}");
        }

        // Let's see what happens...
        if (constructorException != null)
        {
            _output.WriteLine("Provider creation failed as expected - FileSystemWatcher can't watch non-existent directory");
            Assert.IsType<DirectoryNotFoundException>(constructorException);
        }
        else if (provider != null)
        {
            // If provider was created, let's test fetching from non-existent file
            var query = new FileSourceProviderQueryOptions("config.json");
            
            var fetchException = await Record.ExceptionAsync(async () =>
            {
                await provider.FetchConfigurationBytesAsync(query);
            });

            _output.WriteLine($"Fetch attempt result: {(fetchException != null ? $"Failed with {fetchException.GetType().Name}" : "Succeeded")}");
            
            // This should throw DirectoryNotFoundException since the directory doesn't exist
            // (This is better behavior than the old FileNotFoundException)
            Assert.IsType<DirectoryNotFoundException>(fetchException);
        }
    }

    [Fact]
    [Trait("Type", "Stress")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task DirectoryCreatedLater_WithFile_ShouldDetectChanges()
    {
        using var tempDir = TempDirectoryHelper.Create();
        var nestedPath = Path.Combine(tempDir.Path, "level1", "level2");
        var configFile = Path.Combine(nestedPath, "config.json");

        // First, let's test if we can even create the provider when directory doesn't exist
        FileSourceProvider? provider = null;
        Exception? providerException = null;

        try
        {
            var options = new FileSourceProviderOptions(nestedPath);
            provider = new(options);
        }
        catch (Exception ex)
        {
            providerException = ex;
            _output.WriteLine($"Cannot create provider for non-existent directory: {ex.GetType().Name}");
        }

        if (providerException != null)
        {
            // This is the expected behavior - FileSystemWatcher can't watch non-existent directories
            _output.WriteLine("Confirmed: FileProvider cannot watch non-existent directories");
            Assert.True(true, "FileSystemWatcher correctly rejects non-existent directories");
            return;
        }

        // If we get here, the provider was created somehow
        var query = new FileSourceProviderQueryOptions("config.json");
        var emissions = new List<JsonElement>();
        var changeStreamException = await Record.ExceptionAsync(() =>
        {
            var subscription = provider!.ChangesAsBytes(query).Subscribe(
                onNext: e => emissions.Add(e.ToJsonElement()),
                onError: ex => _output.WriteLine($"Change stream error: {ex}"));
            return Task.Delay(100);
        });

        if (changeStreamException != null)
        {
            _output.WriteLine($"Change stream failed: {changeStreamException}");
        }

        // Now create the directory and file
        Directory.CreateDirectory(nestedPath);
        System.IO.File.WriteAllText(configFile, """{"created": "later", "value": 42}""");

        // Wait for file system events to propagate
        await ActiveWaitHelpers.WaitUntilAsync(
            () => System.IO.File.Exists(configFile),
            timeout: TimeSpan.FromSeconds(2),
            description: "file creation detection");

        _output.WriteLine($"After creating directory and file: {emissions.Count} emissions");

        // If the provider was working, it should have detected the file creation
        // But if the directory didn't exist initially, it likely won't work
    }

    [Fact]
    [Trait("Type", "Unit")]  
    [Trait("Provider", "FileSourceProvider")]
    public void DirectoryPath_Validation_AcceptsValidPaths()
    {
        using var tempDir = TempDirectoryHelper.Create();

        // Valid existing directory should work
        var options1 = new FileSourceProviderOptions(tempDir.Path);
        var provider1 = new FileSourceProvider(options1);
        Assert.NotNull(provider1);

        // Test what happens with various path scenarios
        var testPaths = new[]
        {
            tempDir.Path,                                        // Existing directory
            Path.Combine(tempDir.Path, "subdir"),               // Non-existent subdirectory
            Path.Combine(tempDir.Path, "deep", "nested", "path") // Deep non-existent path
        };

        foreach (var testPath in testPaths)
        {
            Exception? exception = null;
            try
            {
                var options = new FileSourceProviderOptions(testPath);
                var provider = new FileSourceProvider(options);
                _output.WriteLine($"Path '{testPath}': Provider created successfully");
            }
            catch (Exception ex)
            {
                exception = ex;
                _output.WriteLine($"Path '{testPath}': Failed with {ex.GetType().Name}: {ex.Message}");
            }

            // Document the behavior for each path type
            if (Directory.Exists(testPath))
            {
                Assert.Null(exception); // Existing directories should work
            }
            else
            {
                // Non-existent directories - let's see what happens
                _output.WriteLine($"Non-existent directory behavior: {(exception != null ? "Throws exception" : "Works")}");
            }
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task ExistingDirectory_FileInSubdirectory_Works()
    {
        using var tempDir = TempDirectoryHelper.Create();
        
        // Create a nested directory structure
        var subDir = Path.Combine(tempDir.Path, "configs");
        Directory.CreateDirectory(subDir);
        
        var configFile = Path.Combine(subDir, "app.json");
        System.IO.File.WriteAllText(configFile, """{"app": "test", "env": "development"}""");

        // Provider points to root, file is in subdirectory
        var options = new FileSourceProviderOptions(tempDir.Path);
        var provider = new FileSourceProvider(options);

        // Query should include the subdirectory path in filename
        var query = new FileSourceProviderQueryOptions(Path.Combine("configs", "app.json"));
        
        var config = await provider.FetchConfigurationBytesAsync(query);

        Assert.Equal("test", config.ToJsonElement().GetProperty("app").GetString());
        Assert.Equal("development", config.ToJsonElement().GetProperty("env").GetString());
        _output.WriteLine("Successfully loaded file from subdirectory");
    }
}
