using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Reactive;

namespace ConfigurationShowcase.Models;

// ── Config types loaded from static JSON ──────────────────────────────────────

/// <summary>Controls which in-flight features are active.</summary>
public class FeatureConfig
{
    public bool NewDashboard { get; set; }
    public bool BetaCheckout { get; set; }
    public bool AdvancedAnalytics { get; set; }
}

/// <summary>Plan / subscription settings that drive entitlement decisions.</summary>
public class PlanConfig
{
    public string Plan { get; set; } = "starter";
    public int MaxApiCallsPerMinute { get; set; } = 100;
    public bool CanExportData { get; set; }
    public int MaxTeamMembers { get; set; } = 5;
}

// ── Feature flags — temporary, expire 2026-09-01 ──────────────────────────────

/// <summary>
/// Application feature flags. Each flag reads config on every call — always fresh.
/// The class-level expiry is a hygiene signal: after this date the Health Dashboard shows Degraded.
/// </summary>
public class AppFeatureFlags : FeatureFlags
{
    public override DateTimeOffset ExpiresAt => new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IReactiveConfig<FeatureConfig> _config;

    /// <summary>Enables the redesigned dashboard UI.</summary>
    public Flag<bool> NewDashboardEnabled { get; }

    /// <summary>Enables the beta checkout flow. Expires before the class.</summary>
    public Flag<bool> BetaCheckoutEnabled { get; }

    /// <summary>Enables advanced analytics (currently expired — triggers Degraded health).</summary>
    public Flag<bool> AdvancedAnalyticsEnabled { get; }

    public AppFeatureFlags(
        IReactiveConfig<FeatureConfig> config,
        IFeatureFlagsRegistry? registry = null) : base(registry)
    {
        _config = config;

        NewDashboardEnabled = DefineFlag(
            nameof(NewDashboardEnabled),
            () => _config.CurrentValue.NewDashboard,
            description: "Enables the redesigned dashboard UI"
        );

        BetaCheckoutEnabled = DefineFlag(
            nameof(BetaCheckoutEnabled),
            () => _config.CurrentValue.BetaCheckout,
            expiresAt: new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            description: "Enables the beta checkout flow (tighter deadline than class)"
        );

        // Intentionally expired to demonstrate health Degraded state
        AdvancedAnalyticsEnabled = DefineFlag(
            nameof(AdvancedAnalyticsEnabled),
            () => _config.CurrentValue.AdvancedAnalytics,
            expiresAt: new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            description: "Advanced analytics dashboard — EXPIRED, clean up this flag"
        );
    }
}

// ── Entitlements — permanent, no expiry ───────────────────────────────────────

/// <summary>
/// Plan-based entitlements. Unlike feature flags, these are permanent —
/// they reflect business rules about what each plan tier may do.
/// </summary>
public class AppPlanEntitlements : Entitlements
{
    private readonly IReactiveConfig<PlanConfig> _config;

    /// <summary>Maximum API calls per minute allowed by the current plan.</summary>
    public Entitlement<int> MaxApiCallsPerMinute { get; }

    /// <summary>Whether the current plan permits data export.</summary>
    public Entitlement<bool> CanExportData { get; }

    /// <summary>Maximum number of team members on the current plan.</summary>
    public Entitlement<int> MaxTeamMembers { get; }

    public AppPlanEntitlements(
        IReactiveConfig<PlanConfig> config,
        IEntitlementsRegistry? registry = null) : base(registry)
    {
        _config = config;

        MaxApiCallsPerMinute = DefineEntitlement(
            nameof(MaxApiCallsPerMinute),
            () => _config.CurrentValue.MaxApiCallsPerMinute,
            description: "Rate limit for API calls on this plan"
        );

        CanExportData = DefineEntitlement(
            nameof(CanExportData),
            () => _config.CurrentValue.CanExportData,
            description: "Whether the plan allows data export"
        );

        MaxTeamMembers = DefineEntitlement(
            nameof(MaxTeamMembers),
            () => _config.CurrentValue.MaxTeamMembers,
            description: "Maximum team size on this plan"
        );
    }
}
