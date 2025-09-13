/*
 * MICROSOFT ADAPTER EXAMPLE
 * 
 * Shows how to use any Microsoft IConfigurationSource with Cocoar.Configuration.
 * This adapter lets you plug existing .NET configuration sources into Cocoar's
 * rule-based configuration system.
 * 
 * Requires: Cocoar.Configuration.MicrosoftAdapter package
 */

using Cocoar.Configuration;
using Cocoar.Configuration.Extensions;
using Cocoar.Configuration.MicrosoftAdapter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Configuration types
public class DatabaseSettings
{
    public string ConnectionString { get; set; } = "";
    public bool EnableRetries { get; set; }
    public int CommandTimeout { get; set; } = 30;
}

public class AppSettings
{
    public string ApplicationName { get; set; } = "";
    public string Version { get; set; } = "";
}

// Service setup using Microsoft adapter
var services = new ServiceCollection();

services.AddCocoarConfiguration(
    // Use Microsoft's in-memory configuration source
    Rule.From.Provider<MicrosoftConfigurationSourceProvider, 
                       MicrosoftConfigurationSourceProviderOptions, 
                       MicrosoftConfigurationSourceProviderQueryOptions>(
        instanceOptions: _ => new MicrosoftConfigurationSourceProviderOptions(
            // You can use ANY Microsoft IConfigurationSource here:
            // - AddJsonFile, AddXmlFile, AddIniFile
            // - AddUserSecrets, AddKeyVault
            // - AddCommandLine, AddEnvironmentVariables
            // - Custom sources, etc.
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
                ["Database:EnableRetries"] = "true",
                ["Database:CommandTimeout"] = "45",
                ["App:ApplicationName"] = "Microsoft Adapter Demo",
                ["App:Version"] = "2.1.0"
            }).Sources[0]
        ),
        queryOptions: _ => new MicrosoftConfigurationSourceProviderQueryOptions(
            keyPrefix: "Database"  // Only pull keys starting with "Database:"
        )
    )
    .For<DatabaseSettings>()
    .Required(),

    // Another Microsoft source for different section
    Rule.From.Provider<MicrosoftConfigurationSourceProvider,
                       MicrosoftConfigurationSourceProviderOptions,
                       MicrosoftConfigurationSourceProviderQueryOptions>(
        instanceOptions: _ => new MicrosoftConfigurationSourceProviderOptions(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:ApplicationName"] = "Microsoft Adapter Demo",
                ["App:Version"] = "2.1.0"
            }).Sources[0]
        ),
        queryOptions: _ => new MicrosoftConfigurationSourceProviderQueryOptions(
            keyPrefix: "App"
        )
    )
    .For<AppSettings>()
    .Required()
);

var serviceProvider = services.BuildServiceProvider();
var configManager = serviceProvider.GetRequiredService<ConfigManager>();

var dbSettings = configManager.GetRequiredConfig<DatabaseSettings>();
var appSettings = configManager.GetRequiredConfig<AppSettings>();

/*
 * REAL-WORLD EXAMPLE - Using Azure Key Vault:
 * 
 * Rule.From.Provider<MicrosoftConfigurationSourceProvider, ...>(
 *     instanceOptions: _ => new MicrosoftConfigurationSourceProviderOptions(
 *         new ConfigurationBuilder()
 *             .AddAzureKeyVault(keyVaultUrl, credential)
 *             .Sources[0]
 *     ),
 *     queryOptions: _ => new MicrosoftConfigurationSourceProviderQueryOptions(
 *         keyPrefix: "ConnectionStrings"
 *     )
 * )
 * 
 * KEY CONCEPTS:
 * - Bridge between Microsoft.Extensions.Configuration and Cocoar.Configuration
 * - Use ANY Microsoft configuration source (JSON, XML, Key Vault, etc.)
 * - Plug into Cocoar's rule-based merging and live recompute
 * - Filter with keyPrefix to only include relevant configuration sections
 * - Combine with other Cocoar providers (File, Environment, HTTP, etc.)
 */