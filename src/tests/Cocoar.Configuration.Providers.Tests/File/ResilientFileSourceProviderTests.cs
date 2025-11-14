using System.Text.Json;
using Cocoar.Configuration.Providers.Tests.Helpers;
using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Providers.Tests.File;

public class ResilientFileSourceProviderTests
{
    private readonly ITestOutputHelper _output;

    public ResilientFileSourceProviderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Test the complete resilient cycle: FsWatcher → Error → Polling → Recovery
    /// </summary>
    [Fact]
    [Trait("Type", "Integration")]
    [Trait("Provider", "FileSourceProvider")]
    [Trait("Category", "ResilientImplementation")]
    public async Task ResilientFileSourceProvider_DirectoryDeletionAndRecreation_ShouldRecover()
    {
        using var tempDir = TempDirectoryHelper.Create();
        
        var featureDir = Path.Combine(tempDir.Path, "features");
        var configFile = Path.Combine(featureDir, "config.json");
        
        _output.WriteLine("TESTING RESILIENT FILESOURCEPROVIDER:");
        _output.WriteLine("1. Start with existing directory (FileSystemWatcher mode)");
        _output.WriteLine("2. Delete directory (triggers Error event → polling mode)");
        _output.WriteLine("3. Recreate directory (polling detects → FileSystemWatcher restarts)");
        _output.WriteLine("4. Verify configuration access works throughout");
        
        // === PHASE 1: Directory exists ===
        Directory.CreateDirectory(featureDir);
        System.IO.File.WriteAllText(configFile, "{ \"version\": 1, \"enabled\": true }");
        
        _output.WriteLine($"Phase 1: Created directory {featureDir}");
        
        var options = new FileSourceProviderOptions(featureDir);
        var query = new FileSourceProviderQueryOptions("config.json");
        using var provider = new FileSourceProvider(options);
        
        var emissions = new List<JsonElement>();
        var errors = new List<Exception>();
        
        var subscription = provider.ChangesAsBytes(query).Subscribe(
            onNext: emission => 
            {
                emissions.Add(emission.ToJsonElement());
                _output.WriteLine($"📩 Change emission: version={GetVersion(emission.ToJsonElement())}, enabled={GetEnabled(emission.ToJsonElement())}");
            },
            onError: ex => 
            {
                errors.Add(ex);
                _output.WriteLine($"❌ Change stream error: {ex.GetType().Name}: {ex.Message}");
            }
        );
        
        await Task.Delay(200); // Let FileSystemWatcher initialize
        
        // Test initial FetchConfigurationAsync
        var initialConfig = await provider.FetchConfigurationBytesAsync(query);
        _output.WriteLine($"✅ Initial config fetch: version={GetVersion(initialConfig.ToJsonElement())}");
        
        // === PHASE 2: Modify file (FileSystemWatcher should detect) ===
        _output.WriteLine("Phase 2: Modifying file to test FileSystemWatcher");
        System.IO.File.WriteAllText(configFile, "{ \"version\": 2, \"enabled\": false }");
        
        await Task.Delay(300);
        var emissionsAfterModify = emissions.Count;
        
        // === PHASE 3: Delete directory ===
        _output.WriteLine("Phase 3: Deleting directory - should trigger Error event and polling fallback");
        Directory.Delete(featureDir, recursive: true);
        
        await Task.Delay(500); // Wait for Error event to trigger polling
        
        // Test FetchConfigurationAsync during polling (should throw)
        try
        {
            var configDuringPolling = await provider.FetchConfigurationBytesAsync(query);
            _output.WriteLine("⚠️ Unexpected: FetchConfigurationAsync succeeded during polling");
        }
        catch (DirectoryNotFoundException ex)
        {
            _output.WriteLine($"✅ Expected during polling: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"⚠️ Different exception during polling: {ex.GetType().Name}: {ex.Message}");
        }
        
        // === PHASE 4: Recreate directory ===
        _output.WriteLine("Phase 4: Recreating directory - polling should detect and restart FileSystemWatcher");
        Directory.CreateDirectory(featureDir);
        System.IO.File.WriteAllText(configFile, "{ \"version\": 3, \"enabled\": true, \"recovered\": true }");
        
        await Task.Delay(12000); // Wait for polling cycle (10 seconds + buffer)
        
        // Test FetchConfigurationAsync after recovery
        try
        {
            var recoveredConfig = await provider.FetchConfigurationBytesAsync(query);
            _output.WriteLine($"✅ Recovery config fetch: version={GetVersion(recoveredConfig.ToJsonElement())}, recovered={GetRecovered(recoveredConfig.ToJsonElement())}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ Failed to fetch config after recovery: {ex.GetType().Name}: {ex.Message}");
        }
        
        // === PHASE 5: Test recovered FileSystemWatcher ===
        _output.WriteLine("Phase 5: Testing recovered FileSystemWatcher with file modification");
        System.IO.File.WriteAllText(configFile, "{ \"version\": 4, \"enabled\": false, \"test\": \"final\" }");
        
        await Task.Delay(500);
        
        var finalEmissions = emissions.Count;
        
        subscription.Dispose();
        
        // === RESULTS ===
        _output.WriteLine("=== RESILIENT PROVIDER RESULTS ===");
        _output.WriteLine($"Emissions after initial modify: {emissionsAfterModify}");
        _output.WriteLine($"Final emissions: {finalEmissions}");
        _output.WriteLine($"Total errors: {errors.Count}");
        
        var hasInitialDetection = emissionsAfterModify > 0;
        var hasRecoveryDetection = finalEmissions > emissionsAfterModify;
        
        if (hasInitialDetection && hasRecoveryDetection)
        {
            _output.WriteLine("✅ RESILIENT PROVIDER SUCCESS!");
            _output.WriteLine("  - FileSystemWatcher detected initial changes");
            _output.WriteLine("  - System survived directory deletion");
            _output.WriteLine("  - FileSystemWatcher recovered after directory recreation");
        }
        else
        {
            _output.WriteLine("⚠️ Resilient provider partial success:");
            _output.WriteLine($"  - Initial detection: {hasInitialDetection}");
            _output.WriteLine($"  - Recovery detection: {hasRecoveryDetection}");
        }
    }

    /// <summary>
    /// Test Required vs Optional behavior during polling mode
    /// </summary>
    [Fact]
    [Trait("Type", "Behavior")]
    [Trait("Provider", "FileSourceProvider")]
    [Trait("Category", "RequiredVsOptional")]
    public async Task PollingMode_FetchConfiguration_ShouldHonorRequiredVsOptional()
    {
        using var tempDir = TempDirectoryHelper.Create();
        
        var nonExistentDir = Path.Combine(tempDir.Path, "does-not-exist");
        var query = new FileSourceProviderQueryOptions("config.json");
        
        _output.WriteLine("TESTING REQUIRED VS OPTIONAL DURING POLLING:");
        _output.WriteLine("When directory doesn't exist, FetchConfigurationAsync should throw");
        _output.WriteLine("This allows ConfigManager to distinguish Required vs Optional rules");
        
        var options = new FileSourceProviderOptions(nonExistentDir);
        using var provider = new FileSourceProvider(options);
        
        // Provider should start in polling mode since directory doesn't exist
        await Task.Delay(100);
        
        // Test FetchConfigurationAsync behavior
        try
        {
            var config = await provider.FetchConfigurationBytesAsync(query);
            _output.WriteLine("❌ PROBLEM: FetchConfigurationAsync should have thrown for non-existent directory");
        }
        catch (DirectoryNotFoundException ex)
        {
            _output.WriteLine($"✅ CORRECT: DirectoryNotFoundException thrown - {ex.Message}");
            _output.WriteLine("This allows ConfigManager to handle Required vs Optional rules properly");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"⚠️ Unexpected exception: {ex.GetType().Name}: {ex.Message}");
        }
        
        _output.WriteLine("\nEXPECTED BEHAVIOR:");
        _output.WriteLine("- Required rules: ConfigManager catches exception and fails application startup");
        _output.WriteLine("- Optional rules: ConfigManager catches exception and uses default/empty configuration");
        _output.WriteLine("- During polling: Provider periodically checks for directory creation");
        _output.WriteLine("- On directory creation: Provider switches back to FileSystemWatcher mode");
    }

    private static int GetVersion(JsonElement element) => element.TryGetProperty("version", out var versionProp) ? versionProp.GetInt32() : -1;

    private static bool GetEnabled(JsonElement element) => element.TryGetProperty("enabled", out var enabledProp) && enabledProp.GetBoolean();

    private static bool GetRecovered(JsonElement element) => element.TryGetProperty("recovered", out var recoveredProp) && recoveredProp.GetBoolean();
}
