namespace Cocoar.Configuration.Flags;

/// <summary>
/// Base class for feature flags. Feature flags are TEMPORARY and MUST have an expiration date.
/// <para>
/// Feature flags answer the question: "Does this code run?"
/// They are technical/operational in nature and owned by Engineering/Ops.
/// </para>
/// <para>
/// A feature flag class is a <b>specific unit</b> — it covers one feature area, one release,
/// or one team's flags. Two teams that need flags should not share a flag class.
/// Because a class is specific, it has exactly <b>one expiry date</b> that applies to all
/// flags within it. Flags with meaningfully different lifecycles belong in separate classes.
/// </para>
/// <para>
/// When <see cref="ExpiresAt"/> is reached, the flags continue to work but
/// <see cref="IsExpired"/> will return true. This is a code hygiene signal
/// indicating that the temporary code should be cleaned up.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>The Litmus Test:</b> "A Feature FeatureFlag without an expiration date is an Entitlement in disguise."
/// </para>
/// <para>
/// If you need permanent feature gating based on business rules (plans, tiers, permissions),
/// use <see cref="Entitlements"/> instead.
/// </para>
/// <para>
/// <b>Descriptions:</b> Document each flag property with an XML doc <c>&lt;summary&gt;</c>.
/// The source generator reads it automatically — no separate attribute or helper method is needed.
/// The same text appears in IDE IntelliSense and in ConfigHub.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class BillingFeatureFlags : FeatureFlags
/// {
///     public override DateTimeOffset ExpiresAt => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
///
///     /// &lt;summary&gt;Enables the redesigned billing dashboard for all users.&lt;/summary&gt;
///     public FeatureFlag&lt;bool&gt; NewDashboardEnabled { get; }
///
///     /// &lt;summary&gt;Gates the new checkout flow for beta users only.&lt;/summary&gt;
///     public FeatureFlag&lt;UserContext, bool&gt; BetaCheckoutForUser { get; }
///
///     public BillingFeatureFlags(IReactiveConfig&lt;BillingConfig&gt; config)
///     {
///         NewDashboardEnabled = () => config.CurrentValue.NewDashboard;
///         BetaCheckoutForUser = user => config.CurrentValue.BetaCheckout &amp;&amp; user.IsBeta;
///     }
/// }
/// </code>
/// </example>
public abstract class FeatureFlags
{
    /// <summary>
    /// When should these flags be removed from code?
    /// After this date, the health API will report them as expired.
    /// The flags continue to work — this is a cleanup reminder.
    /// </summary>
    public abstract DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Is this feature flag class past its expiration?
    /// When true, the flags still work but the code should be cleaned up.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}
