using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Cocoar.Configuration;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.DI;

namespace DIExample;

// ========================= INTERFACES =========================

public interface IPaymentSettings
{
    string ApiKey { get; }
    string MerchantId { get; }
    decimal MaxAmount { get; }
}

public interface IFeatureFlags  
{
    bool EnableNewUI { get; }
    bool EnableBetaFeatures { get; }
    int MaxUsers { get; }
}

public interface IReadOnlyFeatureFlags
{
    bool EnableNewUI { get; }
    bool EnableBetaFeatures { get; }
}

public interface IDatabaseSettings
{
    string ConnectionString { get; }
    int TimeoutSeconds { get; }
}

// ========================= CONCRETE TYPES =========================

public class PaymentSettings : IPaymentSettings
{
    public string ApiKey { get; set; } = "";
    public string MerchantId { get; set; } = "";
    public decimal MaxAmount { get; set; }
    public string InternalNotes { get; set; } = ""; // Not exposed via interface
}

public class FeatureFlags : IFeatureFlags, IReadOnlyFeatureFlags
{
    public bool EnableNewUI { get; set; }
    public bool EnableBetaFeatures { get; set; }
    public int MaxUsers { get; set; } = 100;
    public string ConfigVersion { get; set; } = "1.0"; // Not exposed via interface
}

public class DatabaseSettings : IDatabaseSettings
{
    public string ConnectionString { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool EnableQueryLogging { get; set; } = true; // Not exposed via interface
}

// ========================= SAMPLE SERVICES =========================

public interface IPaymentService
{
    Task<bool> ProcessPaymentAsync(decimal amount);
}

public class PaymentService : IPaymentService
{
    private readonly IPaymentSettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IPaymentSettings settings, ILogger<PaymentService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task<bool> ProcessPaymentAsync(decimal amount)
    {
        _logger.LogInformation("Processing payment of {Amount} with merchant {MerchantId}", 
            amount, _settings.MerchantId);
        
        if (amount > _settings.MaxAmount)
        {
            _logger.LogWarning("Payment amount {Amount} exceeds maximum {MaxAmount}", 
                amount, _settings.MaxAmount);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}

public interface IFeatureService
{
    bool IsFeatureEnabled(string featureName);
}

public class FeatureService : IFeatureService
{
    private readonly IReadOnlyFeatureFlags _flags;
    private readonly ILogger<FeatureService> _logger;

    public FeatureService(IReadOnlyFeatureFlags flags, ILogger<FeatureService> logger)
    {
        _flags = flags;
        _logger = logger;
    }

    public bool IsFeatureEnabled(string featureName)
    {
        var enabled = featureName.ToLowerInvariant() switch
        {
            "newui" => _flags.EnableNewUI,
            "beta" => _flags.EnableBetaFeatures,
            _ => false
        };

        _logger.LogDebug("Feature {FeatureName} is {Status}", featureName, enabled ? "enabled" : "disabled");
        return enabled;
    }
}

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Cocoar.Configuration Advanced DI Example ===");
        Console.WriteLine("(Demonstrating Safe API: Rules → Bindings → Options - Always Works)");
        Console.WriteLine();

        // Build the DI container with Cocoar configuration
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Add Cocoar Configuration with safe, always-working API
        services.AddCocoarConfiguration([
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config/payment.json")).For<PaymentSettings>(),
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config/features.json")).For<FeatureFlags>(),
            Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config/database.json")).For<DatabaseSettings>()
        ], [
            // Simple binding specifications (interface → concrete type mappings)
            Bind.Type<PaymentSettings>().To<IPaymentSettings>(),
            Bind.Type<FeatureFlags>().To<IFeatureFlags>().To<IReadOnlyFeatureFlags>(),
            Bind.Type<DatabaseSettings>().To<IDatabaseSettings>()
        ], options => {
            options.DefaultRegistrationLifetime(ServiceLifetime.Singleton);
            options.Register.Remove<IDatabaseSettings>();
            options.Register.Add<IDatabaseSettings>(ServiceLifetime.Transient);
            
            // Add keyed service for testing
            options.Register.Add<IPaymentSettings>(ServiceLifetime.Scoped, "AlternativeKey");
        });

        // Register application services
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddSingleton<IFeatureService, FeatureService>();

        // Build service provider
        await using var serviceProvider = services.BuildServiceProvider();

        try
        {
            Console.WriteLine("🎯 Testing DI Resolution:");
            Console.WriteLine();

            // Test configuration resolution via DI
            var paymentSettings = serviceProvider.GetRequiredService<IPaymentSettings>();
            Console.WriteLine($"💳 Payment Settings (via DI):");
            Console.WriteLine($"   MerchantId: {paymentSettings.MerchantId}");
            Console.WriteLine($"   MaxAmount: ${paymentSettings.MaxAmount:F2}");
            Console.WriteLine();

            var featureFlags = serviceProvider.GetRequiredService<IFeatureFlags>();
            Console.WriteLine($"🎛️  Feature Flags (via DI):");
            Console.WriteLine($"   EnableNewUI: {featureFlags.EnableNewUI}");
            Console.WriteLine($"   MaxUsers: {featureFlags.MaxUsers}");
            Console.WriteLine();

            var readOnlyFlags = serviceProvider.GetRequiredService<IReadOnlyFeatureFlags>();
            Console.WriteLine($"📖 ReadOnly Feature Flags (via DI):");
            Console.WriteLine($"   EnableBetaFeatures: {readOnlyFlags.EnableBetaFeatures}");
            Console.WriteLine();

            // Test service integration
            Console.WriteLine("🔧 Testing Service Integration:");
            Console.WriteLine();

            var paymentService = serviceProvider.GetRequiredService<IPaymentService>();
            var result = await paymentService.ProcessPaymentAsync(150.00m);
            Console.WriteLine($"   Payment processing result: {(result ? "✅ Success" : "❌ Failed")}");
            Console.WriteLine();

            var featureService = serviceProvider.GetRequiredService<IFeatureService>();
            Console.WriteLine($"   NewUI feature enabled: {featureService.IsFeatureEnabled("newui")}");
            Console.WriteLine($"   Beta feature enabled: {featureService.IsFeatureEnabled("beta")}");
            Console.WriteLine();

            // Test keyed service resolution
            Console.WriteLine("🔑 Testing Keyed Service Registration:");
            var keyedPaymentSettings = serviceProvider.GetRequiredKeyedService<IPaymentSettings>("AlternativeKey");
            Console.WriteLine($"   Keyed IPaymentSettings (AlternativeKey): {keyedPaymentSettings.MerchantId}");
            Console.WriteLine($"   Same instance as regular? {ReferenceEquals(paymentSettings, keyedPaymentSettings)}");
            Console.WriteLine();

            // Test service lifetime verification
            Console.WriteLine("🔄 Testing Service Lifetimes:");
            
            // Test Singleton - should be same instance
            var paymentSettings2 = serviceProvider.GetRequiredService<IPaymentSettings>();
            Console.WriteLine($"   IPaymentSettings Singleton test: {ReferenceEquals(paymentSettings, paymentSettings2)} (should be True)");
            
            // Test Scoped vs Transient - create a scope and check instances
            using (var scope1 = serviceProvider.CreateScope())
            {
                var scopedDb1a = scope1.ServiceProvider.GetRequiredService<IDatabaseSettings>();
                var scopedDb1b = scope1.ServiceProvider.GetRequiredService<IDatabaseSettings>();
                
                // DatabaseSettings is now Transient (due to Remove+Add), so each resolution should be different
                Console.WriteLine($"   IDatabaseSettings Transient test (same scope): {!ReferenceEquals(scopedDb1a, scopedDb1b)} (should be True - different instances)");
            }
            Console.WriteLine();

            // Demonstrate ConfigManager is still available for manual access
            var configManager = serviceProvider.GetRequiredService<ConfigManager>();
            var directAccess = configManager.GetConfig<DatabaseSettings>();
            Console.WriteLine($"🔍 Direct ConfigManager Access:");
            Console.WriteLine($"   Database connection: {directAccess?.ConnectionString?[..20]}***");
            Console.WriteLine($"   Query logging enabled: {directAccess?.EnableQueryLogging}");
            Console.WriteLine();

        Console.WriteLine("✨ Key Safe API Benefits:");
        Console.WriteLine("   ✅ Rules: Define where configuration comes from");
        Console.WriteLine("   ✅ Bindings: Map interfaces to concrete types (core feature)");
        Console.WriteLine("   ✅ Optional Options: Add when needed - works without them");
        Console.WriteLine("   ✅ Auto-registration: All rule types and binding interfaces registered with default lifetime");
        Console.WriteLine("   ✅ Disable auto-registration: options.DefaultRegistrationLifetime(null)");
        Console.WriteLine("   ✅ Fine-grained control: options.Register.Remove<T>() and .Add<T>(lifetime)");
        Console.WriteLine("   ✅ Unified API: Only Add and Remove methods - no redundant AsSingleton/AsScoped/AsTransient");
        Console.WriteLine("   ✅ ConfigManager overload: Pass pre-built instance for full control");
        Console.WriteLine("   ✅ Fail-safe: Impossible to forget method calls - always works");
        Console.WriteLine("   ✅ Maximum flexibility with zero footguns");        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
        }
    }
}
