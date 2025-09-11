using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.MicrosoftAdapter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

public class MicrosoftSourcesIntegrationTests
{
    private sealed class DemoConfig
    {
        public bool Enabled { get; set; }
        public int Value { get; set; }
    }

    [Fact]
    public async Task JsonFile_changes_trigger_recompute_via_configmanager()
    {
        var dir = Directory.CreateTempSubdirectory();
        var file = Path.Combine(dir.FullName, "appsettings.json");
        await File.WriteAllTextAsync(file, "{\n  \"My\": { \"Section\": { \"Enabled\": true, \"Value\": 1 } }\n}");

        // Build a Microsoft JSON source with reloads, then adapt via Rules.FromMicrosoftSource
        var basePath = Path.GetDirectoryName(file)!;
        var fileName = Path.GetFileName(file);
        var jsonSource = new ConfigurationBuilder()
            .AddJsonFile(fileName, optional: true, reloadOnChange: true)
            .Sources[^1];

        var rules = new[]
        {
            Rules.FromProvider<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions, MicrosoftConfigurationSourceProviderQueryOptions>(
                instanceOptions: _ => new MicrosoftConfigurationSourceProviderOptions(jsonSource, basePath: basePath),
                queryOptions:    _ => new MicrosoftConfigurationSourceProviderQueryOptions(configurationPrefix: "My:Section")
            )
            .For<DemoConfig>()
            .Required()
            .Build(),
        };

        var mgr = new ConfigManager(rules, NullLogger.Instance).Initialize();
        var initial = mgr.GetConfig<DemoConfig>();
        Assert.NotNull(initial);
    Assert.True(initial.Enabled);
        Assert.Equal(1, initial.Value);

        // mutate file
        await File.WriteAllTextAsync(file, "{\n  \"My\": { \"Section\": { \"Enabled\": false, \"Value\": 2 } }\n}");

        // Actively poll until the change propagates (up to 8s)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DemoConfig? updated = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            await Task.Delay(200);
            updated = mgr.GetConfig<DemoConfig>();
            if (updated?.Value == 2 && updated.Enabled == false) break;
        }
        Assert.NotNull(updated);
    Assert.False(updated.Enabled);
        Assert.Equal(2, updated.Value);

        dir.Delete(recursive: true);
    }

    [Fact]
    public async Task IniFile_changes_trigger_recompute_via_configmanager()
    {
        var dir = Directory.CreateTempSubdirectory();
        var file = Path.Combine(dir.FullName, "appsettings.ini");
        await File.WriteAllTextAsync(file, "[My:Section]\nEnabled=true\nValue=1\n");

        var basePath = Path.GetDirectoryName(file)!;
        var fileName = Path.GetFileName(file);
        var iniSource = new ConfigurationBuilder()
            .AddIniFile(fileName, optional: true, reloadOnChange: true)
            .Sources[^1];

        var rules = new[]
        {
            Rules.FromProvider<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions, MicrosoftConfigurationSourceProviderQueryOptions>(
                instanceOptions: _ => new MicrosoftConfigurationSourceProviderOptions(iniSource, basePath: basePath),
                queryOptions:    _ => new MicrosoftConfigurationSourceProviderQueryOptions(configurationPrefix: "My:Section")
            )
            .For<DemoConfig>()
            .Required()
            .Build(),
        };

        var mgr = new ConfigManager(rules, NullLogger.Instance).Initialize();
        var initial = mgr.GetConfig<DemoConfig>();
        Assert.NotNull(initial);
        Assert.True(initial!.Enabled);
        Assert.Equal(1, initial.Value);

        // Give the underlying FileSystemWatcher time to fully subscribe (Linux CI sometimes needs a brief settle)
        await Task.Delay(250);

        // Mutate file content (ensure actual textual difference)
        await File.WriteAllTextAsync(file, "[My:Section]\nEnabled=false\nValue=3\n");

        // Actively wait for change token propagation (up to 8s)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DemoConfig? updated = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            await Task.Delay(200);
            updated = mgr.GetConfig<DemoConfig>();
            if (updated?.Value == 3 && updated.Enabled == false) break;
        }
        Assert.NotNull(updated);
        Assert.False(updated!.Enabled);
        Assert.Equal(3, updated.Value);

        dir.Delete(recursive: true);
    }

    [Fact]
    public void EnvVariables_do_not_auto_recompute_but_snapshot_is_read()
    {
        // arrange temporary environment vars with a unique prefix
        var prefix = "MSCFG_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(prefix + "My__Section__Enabled", "true");
        Environment.SetEnvironmentVariable(prefix + "My__Section__Value", "7");

        try
        {
            var envSource = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix)
                .Sources[^1];

            var rules = new[]
            {
                Rules.FromProvider<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions, MicrosoftConfigurationSourceProviderQueryOptions>(
                    instanceOptions: _ => new MicrosoftConfigurationSourceProviderOptions(envSource),
                    queryOptions:    _ => new MicrosoftConfigurationSourceProviderQueryOptions(configurationPrefix: "My:Section")
                )
                .For<DemoConfig>()
                .Required()
                .Build(),
            };

            var mgr = new ConfigManager(rules, NullLogger.Instance).Initialize();
            var cfg = mgr.GetConfig<DemoConfig>();
            Assert.NotNull(cfg);
        Assert.True(cfg.Enabled);
            Assert.Equal(7, cfg.Value);

            // update env var; Microsoft provider has no change token for env vars
            Environment.SetEnvironmentVariable(prefix + "My__Section__Value", "9");
            // no recompute expected automatically; snapshot remains old
            var stillOld = mgr.GetConfig<DemoConfig>();
            Assert.Equal(7, stillOld!.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "My__Section__Enabled", null);
            Environment.SetEnvironmentVariable(prefix + "My__Section__Value", null);
        }
    }
}
