using System.Net;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets;
using Microsoft.AspNetCore.Mvc;

namespace ShowCase;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddCocoarConfiguration(c => c
            .UseConfiguration(rule => [
                // Existing: startup config from file + environment overrides
                rule.For<StartUpConfiguration>().FromFile("config.json").Select("Startup"),
                rule.For<StartUpConfiguration>().FromEnvironment(),

                // Feature flags config — values flipped here to demo on/off behaviour
                rule.For<FeatureConfig>().FromStaticJson("""
                    {
                        "NewDashboard": false,
                        "BetaCheckout": true
                    }
                    """),

                // Plan config — change "Plan" or "CanExportData" to see entitlements react
                rule.For<PlanConfig>().FromStaticJson("""
                    {
                        "Plan": "starter",
                        "MaxApiCallsPerMinute": 100,
                        "CanExportData": false
                    }
                    """),
            ])
            .UseSecretsSetup(secrets => secrets
                .UseCertificatesFromFolder("certs"))
            .UseFeatureFlags(f => f.Register<AppFeatureFlags>())
            .UseEntitlements(e => e.Register<AppPlanEntitlements>()));

        var app = builder.Build();

        // ── Existing endpoints ────────────────────────────────────────────────

        app.MapGet("/", () => "Hello World!");

        app.MapGet("/creds", (StartUpConfiguration conf) =>
        {
            var secret = conf.MySecret.Open().Value;
            if (!int.TryParse(secret, out var secretValue))
                return $"Invalid secret format: expected integer, got '{secret}'";

            var networkCredsConfig = conf.Credentials.Open().Value;
            var networkCreds = networkCredsConfig.ToNetworkCredential();
            return $"Username: {networkCreds.UserName}, Domain: {networkCreds.Domain}, Password Length: {networkCreds.Password.Length}";
        });

        // ── Feature flags endpoints ───────────────────────────────────────────

        // Current value + metadata for every flag in AppFeatureFlags
        app.MapGet("/flags", (AppFeatureFlags flags) => new
        {
            ExpiresAt = flags.ExpiresAt,
            IsExpired = flags.IsExpired,
            Flags = new[]
            {
                new
                {
                    Name      = nameof(flags.NewDashboardEnabled),
                    Value     = flags.NewDashboardEnabled(),
                    ExpiresAt = flags.GetMetadata(flags.NewDashboardEnabled)?.ExpiresAt,
                    IsExpired = flags.GetMetadata(flags.NewDashboardEnabled)?.IsExpired,
                    Description = flags.GetMetadata(flags.NewDashboardEnabled)?.Description
                },
                new
                {
                    Name      = nameof(flags.BetaCheckoutEnabled),
                    Value     = flags.BetaCheckoutEnabled(),
                    ExpiresAt = flags.GetMetadata(flags.BetaCheckoutEnabled)?.ExpiresAt,
                    IsExpired = flags.GetMetadata(flags.BetaCheckoutEnabled)?.IsExpired,
                    Description = flags.GetMetadata(flags.BetaCheckoutEnabled)?.Description
                }
            }
        });

        // Current plan entitlements
        app.MapGet("/plan", (AppPlanEntitlements entitlements) => new
        {
            MaxApiCallsPerMinute = entitlements.MaxApiCallsPerMinute(),
            CanExportData        = entitlements.CanExportData()
        });

        // Demonstrates conditional behaviour driven by a feature flag
        app.MapGet("/checkout", (AppFeatureFlags flags) =>
            flags.BetaCheckoutEnabled()
                ? "🚀 Using new beta checkout flow"
                : "Using standard checkout flow");

        // Demonstrates a business rule enforced by an entitlement
        app.MapGet("/export", (AppPlanEntitlements entitlements) =>
            entitlements.CanExportData()
                ? "✅ Export started"
                : "⛔ Export not available on your current plan (upgrade to Pro)");

        // Registry overview — all registered flag descriptors and their expiry state
        app.MapGet("/flags/registry", (IFeatureFlagsRegistry registry) =>
            registry.GetDescriptors().Select(d => new
            {
                Type      = d.Type.Name,
                ExpiresAt = d.ExpiresAt,
                IsExpired = d.IsExpired,
                Flags     = d.Flags.Select(f => new
                {
                    f.Name,
                    f.ExpiresAt,
                    f.IsExpired,
                    f.Description
                })
            }));

        app.Run();
    }
}


public class NetworkCredentialConfig
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";  // Encrypt this!
    public string? Domain { get; set; }

    public NetworkCredential ToNetworkCredential()
        => new(UserName, Password, Domain);
}
