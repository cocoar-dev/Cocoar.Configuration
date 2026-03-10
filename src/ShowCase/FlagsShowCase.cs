using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Reactive;

namespace ShowCase;

// ──────────────────────────────────────────────────────────────────────────────
// Config types — loaded from JSON, drive flag and entitlement evaluation
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Controls which in-flight features are active.
/// Values come from configuration — change the JSON to flip flags at runtime.
/// </summary>
public class FeatureConfig
{
    public bool NewDashboard { get; set; } = false;
    public bool BetaCheckout { get; set; } = true;
}

/// <summary>
/// Plan / subscription settings that drive entitlement decisions.
/// </summary>
public class PlanConfig
{
    public string Plan { get; set; } = "starter";
    public int MaxApiCallsPerMinute { get; set; } = 100;
    public bool CanExportData { get; set; } = false;
}

// ──────────────────────────────────────────────────────────────────────────────
// Feature flags — temporary, expire 2026-09-01
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Application feature flags. Flags read config on every call — no caching, always fresh.
/// Each flag has an expiry; after that date the health API reports Degraded as a cleanup reminder.
/// </summary>
public class AppFeatureFlags : FeatureFlags
{
    // When this whole class of flags should be cleaned up from the codebase.
    public override DateTimeOffset ExpiresAt => new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IReactiveConfig<FeatureConfig> _config;

    /// <summary>Enables the redesigned dashboard UI.</summary>
    public Flag<bool> NewDashboardEnabled { get; }

    /// <summary>Enables the beta checkout flow. Expires sooner than the class.</summary>
    public Flag<bool> BetaCheckoutEnabled { get; }

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
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Entitlements — permanent, no expiry
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Plan-based entitlements. Unlike feature flags, these are permanent — they
/// reflect business rules about what each plan tier is allowed to do.
/// </summary>
public class AppPlanEntitlements : Entitlements
{
    private readonly IReactiveConfig<PlanConfig> _config;

    /// <summary>Maximum API calls per minute allowed by the current plan.</summary>
    public Entitlement<int> MaxApiCallsPerMinute { get; }

    /// <summary>Whether the current plan permits data export.</summary>
    public Entitlement<bool> CanExportData { get; }

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
    }
}
