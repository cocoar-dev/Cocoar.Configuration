using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers; // FromStaticJson / FromFile

namespace Examples.MultiTenancyExample;

// Demonstrates MULTI-TENANCY: the SAME configuration type resolves to a DIFFERENT value per tenant,
// layered on a shared global base. You author ONE flat rule list:
//   - global rules apply to everyone and form the base each tenant inherits
//   - rules marked .TenantScoped() run ONLY inside a tenant pipeline; the tenant id flows in via accessor.Tenant
//   - each tenant's overlay is SPARSE — it overrides only the keys it sets and inherits the rest
public static class Program
{
    public sealed record Branding
    {
        public string ProductName { get; init; } = "";
        public string Theme { get; init; } = "";
        public string SupportEmail { get; init; } = "";
    }

    public static async Task Main()
    {
        // Self-contained demo: write two tiny SPARSE per-tenant override files next to the binary.
        var tenantDir = Path.Combine(AppContext.BaseDirectory, "tenants");
        Directory.CreateDirectory(tenantDir);
        await File.WriteAllTextAsync(
            Path.Combine(tenantDir, "contoso.json"),
            """{ "Theme": "contoso-dark", "SupportEmail": "support@contoso.example" }""");
        await File.WriteAllTextAsync(
            Path.Combine(tenantDir, "globex.json"),
            """{ "Theme": "globex-neon" }"""); // overrides ONLY Theme — everything else inherits the base

        using var manager = ConfigManager.Create(c => c.UseConfiguration(rules =>
        [
            // GLOBAL base — applies to everyone; tenants inherit any key they don't override.
            rules.For<Branding>().FromStaticJson(
                """{ "ProductName": "Acme Cloud", "Theme": "light", "SupportEmail": "support@acme.example" }"""),

            // TENANT overlay — ONE rule serves every tenant: the tenant id flows into the file path via
            // accessor.Tenant. Skipped entirely in the global pipeline (there is no tenant there).
            rules.For<Branding>().FromFile(a => $"tenants/{a.Tenant}.json").TenantScoped(),
        ]));

        // Tenants are explicit and host-owned: initialize, then read synchronously.
        var tenants = (ITenantConfigurationAccessor)manager;
        await tenants.InitializeTenantAsync("contoso");
        await tenants.InitializeTenantAsync("globex");

        // GLOBAL read: tenant overlays are skipped → the pure base.
        Print("global (no tenant)", manager.GetConfig<Branding>()!);

        // PER-TENANT reads: the overlay wins per key, the rest inherits the base.
        Print("contoso", manager.GetConfigForTenant<Branding>("contoso")!); // Theme + SupportEmail overridden
        Print("globex", manager.GetConfigForTenant<Branding>("globex")!);   // only Theme overridden; the rest inherited

        Console.WriteLine();
        Console.WriteLine("ProductName is inherited from the global base by every tenant (sparse overlay);");
        Console.WriteLine("globex inherits SupportEmail too — it only overrode Theme.");
    }

    private static void Print(string label, Branding b) =>
        Console.WriteLine($"{label,-18} ProductName={b.ProductName,-12} Theme={b.Theme,-14} Support={b.SupportEmail}");
}
