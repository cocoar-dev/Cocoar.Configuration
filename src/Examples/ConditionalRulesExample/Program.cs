using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

Console.WriteLine("=== Conditional Rules Example ===\n");
Console.WriteLine("Demonstrates using When() with IConfigurationAccessor\n");

// Setup: Load tenant configuration, then conditionally load premium features
var manager = new ConfigManager(rule => [
    // Load tenant info first
    rule.StaticJson("""
    {
        "TenantId": "acme-corp",
        "Tier": "Premium"
    }
    """).For<TenantSettings>(),
    
    // Conditionally load premium features only for Premium tier tenants
    rule.StaticJson("""
    {
        "AdvancedAnalytics": true,
        "PrioritySupport": true
    }
    """)
        .When(accessor =>
        {
            var tenant = accessor.GetRequiredConfig<TenantSettings>();
            return tenant.Tier == "Premium";
        })
        .For<PremiumFeatures>()
]).Initialize();

var tenant = manager.GetRequiredConfig<TenantSettings>();
var features = manager.GetConfig<PremiumFeatures>();

Console.WriteLine($"Tenant: {tenant.TenantId}");
Console.WriteLine($"Tier: {tenant.Tier}");
Console.WriteLine($"Premium Features: {(features != null ? "Enabled ✓" : "Not Available")}");

if (features != null)
{
    Console.WriteLine($"  - Advanced Analytics: {features.AdvancedAnalytics}");
    Console.WriteLine($"  - Priority Support: {features.PrioritySupport}");
}

Console.WriteLine("\n=== Example Complete ===");

// Configuration classes
public record TenantSettings
{
    public string TenantId { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
}

public record PremiumFeatures
{
    public bool AdvancedAnalytics { get; set; }
    public bool PrioritySupport { get; set; }
}
