using Cocoar.Configuration.Providers.Tests.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Cocoar.Configuration.Providers.Tests.File;

/// <summary>
/// Security tests for FileSourceProvider: path traversal (S-01) and symlink rejection (S-02).
/// These validate that the provider refuses to read files outside its configured directory.
/// </summary>
public class FileProviderSecurityTests
{
    private readonly ITestOutputHelper _output;

    public FileProviderSecurityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ──────────────────────────────────────────────
    // S-01: Path traversal
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task PathTraversal_DotDotSlash_ThrowsUnauthorizedAccess()
    {
        using var tempDir = TempDirectoryHelper.Create();
        // Create a file in the parent directory (outside the configured base)
        var parentDir = Path.GetDirectoryName(tempDir.Path)!;
        var secretFile = Path.Combine(parentDir, "secret.json");
        System.IO.File.WriteAllText(secretFile, """{"leaked": true}""");

        try
        {
            var provider = new FileSourceProvider(new FileSourceProviderOptions(tempDir.Path));
            var query = new FileSourceProviderQueryOptions("../secret.json");

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => provider.FetchConfigurationBytesAsync(query));

            Assert.Contains("Path traversal detected", ex.Message);
            _output.WriteLine($"Correctly blocked: {ex.Message}");
        }
        finally
        {
            System.IO.File.Delete(secretFile);
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task PathTraversal_DeepDotDot_ThrowsUnauthorizedAccess()
    {
        using var tempDir = TempDirectoryHelper.Create();

        var provider = new FileSourceProvider(new FileSourceProviderOptions(tempDir.Path));
        var query = new FileSourceProviderQueryOptions("sub/../../outside.json");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => provider.FetchConfigurationBytesAsync(query));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task PathTraversal_SimilarPrefixDirectory_ThrowsUnauthorizedAccess()
    {
        // Regression: "config_backup" starts with "config" — without trailing separator
        // check, a base dir "config" would match "config_backup/../secret.json".
        using var parentDir = TempDirectoryHelper.Create();
        var configDir = Path.Combine(parentDir.Path, "config");
        var configBackupDir = Path.Combine(parentDir.Path, "config_backup");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(configBackupDir);

        var secretPath = Path.Combine(configBackupDir, "secret.json");
        System.IO.File.WriteAllText(secretPath, """{"leaked": true}""");

        try
        {
            var provider = new FileSourceProvider(new FileSourceProviderOptions(configDir));
            // This path resolves to config_backup/secret.json — outside "config/"
            var query = new FileSourceProviderQueryOptions("../config_backup/secret.json");

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => provider.FetchConfigurationBytesAsync(query));

            Assert.Contains("Path traversal detected", ex.Message);
            _output.WriteLine($"Similar-prefix attack blocked: {ex.Message}");
        }
        finally
        {
            System.IO.File.Delete(secretPath);
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task NormalFile_InsideConfiguredDirectory_Succeeds()
    {
        using var tempDir = TempDirectoryHelper.Create();
        using var file = TempFileHelper.CreateInDirectory(tempDir.Path, "app.json", """{"ok": true}""");

        var provider = new FileSourceProvider(new FileSourceProviderOptions(tempDir.Path));
        var query = new FileSourceProviderQueryOptions("app.json");

        var bytes = await provider.FetchConfigurationBytesAsync(query);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task Subdirectory_InsideConfiguredDirectory_Succeeds()
    {
        using var tempDir = TempDirectoryHelper.Create();
        var subDir = Path.Combine(tempDir.Path, "sub");
        Directory.CreateDirectory(subDir);
        System.IO.File.WriteAllText(Path.Combine(subDir, "nested.json"), """{"nested": true}""");

        var provider = new FileSourceProvider(new FileSourceProviderOptions(tempDir.Path));
        var query = new FileSourceProviderQueryOptions("sub/nested.json");

        var bytes = await provider.FetchConfigurationBytesAsync(query);
        Assert.True(bytes.Length > 0);
    }

    // ──────────────────────────────────────────────
    // S-02: Symlink rejection
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task Symlink_ToFileOutsideDirectory_ThrowsUnauthorizedAccess()
    {
        if (!CanCreateSymlinks())
        {
            _output.WriteLine("Skipping: symlink creation requires elevated privileges on this OS");
            return;
        }

        using var tempDir = TempDirectoryHelper.Create();
        using var outsideDir = TempDirectoryHelper.Create();

        // Create a real file outside the config directory
        var outsideFile = Path.Combine(outsideDir.Path, "secret.json");
        System.IO.File.WriteAllText(outsideFile, """{"leaked": true}""");

        // Create a symlink inside the config directory pointing to the outside file
        var symlinkPath = Path.Combine(tempDir.Path, "linked.json");
        System.IO.File.CreateSymbolicLink(symlinkPath, outsideFile);

        var provider = new FileSourceProvider(new FileSourceProviderOptions(tempDir.Path));
        var query = new FileSourceProviderQueryOptions("linked.json");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => provider.FetchConfigurationBytesAsync(query));

        Assert.Contains("Symlinks are not allowed", ex.Message);
        _output.WriteLine($"Symlink attack blocked: {ex.Message}");
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "FileSourceProvider")]
    public async Task Symlink_ToFileInsideDirectory_StillRejected()
    {
        // Symlinks are rejected regardless of target — defense in depth
        if (!CanCreateSymlinks())
        {
            _output.WriteLine("Skipping: symlink creation requires elevated privileges on this OS");
            return;
        }

        using var tempDir = TempDirectoryHelper.Create();
        var realFile = Path.Combine(tempDir.Path, "real.json");
        System.IO.File.WriteAllText(realFile, """{"real": true}""");

        var symlinkPath = Path.Combine(tempDir.Path, "link.json");
        System.IO.File.CreateSymbolicLink(symlinkPath, realFile);

        var provider = new FileSourceProvider(new FileSourceProviderOptions(tempDir.Path));
        var query = new FileSourceProviderQueryOptions("link.json");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => provider.FetchConfigurationBytesAsync(query));

        Assert.Contains("Symlinks are not allowed", ex.Message);
    }

    private static bool CanCreateSymlinks()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "cocoar_symlink_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            var target = Path.Combine(testDir, "target.txt");
            System.IO.File.WriteAllText(target, "test");
            var link = Path.Combine(testDir, "link.txt");

            try
            {
                System.IO.File.CreateSymbolicLink(link, target);
                return System.IO.File.Exists(link);
            }
            catch
            {
                return false;
            }
        }
        finally
        {
            try { Directory.Delete(testDir, recursive: true); } catch { }
        }
    }
}
