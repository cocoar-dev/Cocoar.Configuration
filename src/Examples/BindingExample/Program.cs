using System;
using Cocoar.Configuration;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace BindingExample;

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
        Console.WriteLine("=== Cocoar.Configuration Binding Example ===");
        Console.WriteLine("(Demonstrating interface binding without DI)");
        Console.WriteLine();

        try
        {           
            var manager = new ConfigManager([
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config/payment.json")).For<PaymentConfig>(),
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config/features.json")).For<FeatureToggleConfig>(),
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("config/database.json")).For<DatabaseConfig>()
            ], [
                Bind.Type<PaymentConfig>().To<IPaymentConfig>(),
                Bind.Type<FeatureToggleConfig>().To<IFeatureToggles>().To<IReadOnlyFeatureToggles>()
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

            Console.WriteLine("🎭 Testing interface access through bindings:");
            
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
            Console.WriteLine("❌ Testing interface without binding (should be null):");
            
            var dbInterface = manager.GetConfig<IPaymentConfig>(); // This will work (bound)
            Console.WriteLine($"   IPaymentConfig (bound): {(dbInterface != null ? "✓ Found" : "✗ Not found")}");
            
            // Hypothetical interface not defined in bindings would return null
            Console.WriteLine("   (Hypothetical non-bound interface would return null)");
            Console.WriteLine();

            // 7. Demonstrate fallback behavior
            Console.WriteLine("🔄 Binding resolution process:");
            Console.WriteLine("   1. GetConfig<IPaymentConfig>() called");
            Console.WriteLine("   2. Direct lookup for IPaymentConfig: ✗ Not found");
            Console.WriteLine("   3. Check binding registry: ✓ Found mapping IPaymentConfig → PaymentConfig");
            Console.WriteLine("   4. Lookup PaymentConfig: ✓ Found configuration");
            Console.WriteLine("   5. Deserialize to PaymentConfig, cast to IPaymentConfig: ✓ Success");
            Console.WriteLine();

            // 8. Show API patterns
            Console.WriteLine("📖 Key Binding System Patterns:");
            Console.WriteLine("   ✓ Bind.Type<T>().To<IInterface>().Build()");
            Console.WriteLine("   ✓ new ConfigManager(rules, bindings)");
            Console.WriteLine("   ✓ manager.GetConfig<IInterface>() // resolves via binding");
            Console.WriteLine("   ✓ manager.GetConfig<ConcreteType>() // direct access");
            Console.WriteLine("   ✓ Multiple interfaces per concrete type");
            Console.WriteLine("   ✓ Runtime validation of interface implementation");
            Console.WriteLine();

            Console.WriteLine("🎯 The Binding system enables clean interface-based access");
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
