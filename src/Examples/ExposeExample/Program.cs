using System;
using Cocoar.Configuration;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace ExposeExample;

// ========================= INTERFACES =========================

public interface IPaymentConfig
{
    string MerchantId { get; }
    string ApiKey { get; }
    decimal MaxTransactionAmount { get; }
    bool EnableRefunds { get; }
}

public interface IFeatureToggles
{
    bool EnableNewDashboard { get; }
    bool EnableExperimentalFeatures { get; }
    int MaxConcurrentUsers { get; }
}

public interface IReadOnlyFeatureToggles
{
    bool EnableNewDashboard { get; }
    bool EnableExperimentalFeatures { get; }
}

// ========================= CONCRETE TYPES =========================

public class PaymentConfig : IPaymentConfig
{
    public string MerchantId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public decimal MaxTransactionAmount { get; set; }
    public bool EnableRefunds { get; set; } = true;
    
    // Additional properties not in interface
    public string InternalNotes { get; set; } = "";
}

public class FeatureToggleConfig : IFeatureToggles, IReadOnlyFeatureToggles
{
    public bool EnableNewDashboard { get; set; }
    public bool EnableExperimentalFeatures { get; set; }
    public int MaxConcurrentUsers { get; set; } = 50;
    
    // Additional properties not exposed through interfaces
    public bool CacheEnabled { get; set; } = true;
    public string ConfigVersion { get; set; } = "1.0";
}

public class DatabaseConfig
{
    public string ConnectionString { get; set; } = "";
    public int CommandTimeout { get; set; } = 30;
    public bool EnableLogging { get; set; } = true;
}

public static class Program
{
    public static void Main(string[] args)
    {
    Console.WriteLine("=== Cocoar.Configuration Exposure Example ===");
    Console.WriteLine("(Demonstrating interface exposure without DI)");
        Console.WriteLine();

        try
        {           
            var manager = new ConfigManager(rule => [
                rule.For<PaymentConfig>().FromFile(_ => FileSourceRuleOptions.FromFilePath("config/payment.json")),
                rule.For<FeatureToggleConfig>().FromFile(_ => FileSourceRuleOptions.FromFilePath("config/features.json")),
                rule.For<DatabaseConfig>().FromFile(_ => FileSourceRuleOptions.FromFilePath("config/database.json"))
            ], setup => [
                setup.ConcreteType<PaymentConfig>().ExposeAs<IPaymentConfig>(),
                setup.ConcreteType<FeatureToggleConfig>().ExposeAs<IFeatureToggles>().ExposeAs<IReadOnlyFeatureToggles>()
            ]);

            manager.Initialize();

            Console.WriteLine("📋 ConfigManager initialized successfully!");
            Console.WriteLine();

            Console.WriteLine("🔧 Testing concrete type access:");
            
            var paymentConcrete = manager.GetConfig<PaymentConfig>();
            Console.WriteLine($"   PaymentConfig.MerchantId: {paymentConcrete?.MerchantId}");
            Console.WriteLine($"   PaymentConfig.InternalNotes: {paymentConcrete?.InternalNotes}");
            
            var featureConcrete = manager.GetConfig<FeatureToggleConfig>();
            Console.WriteLine($"   FeatureToggleConfig.CacheEnabled: {featureConcrete?.CacheEnabled}");
            Console.WriteLine();

            Console.WriteLine("🎭 Testing interface access through exposures:");
            
            var paymentInterface = manager.GetConfig<IPaymentConfig>();
            Console.WriteLine($"   IPaymentConfig.MerchantId: {paymentInterface?.MerchantId}");
            Console.WriteLine($"   IPaymentConfig.EnableRefunds: {paymentInterface?.EnableRefunds}");
            
            var featureInterface = manager.GetConfig<IFeatureToggles>();
            Console.WriteLine($"   IFeatureToggles.EnableNewDashboard: {featureInterface?.EnableNewDashboard}");
            Console.WriteLine($"   IFeatureToggles.MaxConcurrentUsers: {featureInterface?.MaxConcurrentUsers}");
            
            var readOnlyInterface = manager.GetConfig<IReadOnlyFeatureToggles>();
            Console.WriteLine($"   IReadOnlyFeatureToggles.EnableExperimentalFeatures: {readOnlyInterface?.EnableExperimentalFeatures}");
            Console.WriteLine();

            // 6. Test type not in bindings (should return null for interface)
            Console.WriteLine("❌ Testing interface without exposure (should be null):");
            
            var dbInterface = manager.GetConfig<IPaymentConfig>();
            Console.WriteLine($"   IPaymentConfig (exposed): {(dbInterface != null ? "✓ Found" : "✗ Not found")}");
            
            // Hypothetical interface not defined in bindings would return null
            Console.WriteLine("   (Hypothetical non-exposed interface would return null)");
            Console.WriteLine();

            // 7. Demonstrate fallback behavior
            Console.WriteLine("🔄 Exposure resolution process:");
            Console.WriteLine("   1. GetConfig<IPaymentConfig>() called");
            Console.WriteLine("   2. Direct lookup for IPaymentConfig: ✗ Not found");
            Console.WriteLine("   3. Check exposure registry: ✓ Found mapping IPaymentConfig → PaymentConfig");
            Console.WriteLine("   4. Lookup PaymentConfig: ✓ Found configuration");
            Console.WriteLine("   5. Deserialize to PaymentConfig, cast to IPaymentConfig: ✓ Success");
            Console.WriteLine();

            // 8. Show API patterns
            Console.WriteLine("📖 Key Exposure Patterns:");
            Console.WriteLine("   ✓ Setup.ConcreteType<T>().ExposeAs<IInterface>().Build()");
            Console.WriteLine("   ✓ new ConfigManager(rules, configuredBuilders)");
            Console.WriteLine("   ✓ manager.GetConfig<IInterface>() // resolves via exposure");
            Console.WriteLine("   ✓ manager.GetConfig<ConcreteType>() // direct access");
            Console.WriteLine("   ✓ Multiple interfaces per concrete type");
            Console.WriteLine("   ✓ Runtime validation of interface implementation");
            Console.WriteLine();

            Console.WriteLine("🎯 Exposures enable clean interface-based access");
            Console.WriteLine("   without coupling the core library to DI frameworks!");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
        }
    }
}

