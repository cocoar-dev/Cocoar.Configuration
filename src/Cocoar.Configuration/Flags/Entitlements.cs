namespace Cocoar.Configuration.Flags;

/// <summary>
/// Base class for entitlements. Entitlements are PERMANENT and have no expiration.
/// <para>
/// Entitlements answer the question: "May this actor do this?"
/// They are business/domain in nature and owned by Product/Business.
/// </para>
/// <para>
/// Unlike <see cref="FeatureFlags"/>, entitlements represent permanent product logic
/// such as plan tiers, feature availability, and permission-based access.
/// </para>
/// <para>
/// <b>Descriptions:</b> Document each entitlement property with an XML doc <c>&lt;summary&gt;</c>.
/// The source generator reads it automatically — no separate attribute or helper method is needed.
/// The same text appears in IDE IntelliSense and in ConfigHub.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>When to use Entitlements vs FeatureFlags:</b>
/// </para>
/// <list type="bullet">
///   <item>Use <see cref="FeatureFlags"/> for temporary technical rollouts (A/B tests, gradual rollouts, kill switches)</item>
///   <item>Use <see cref="Entitlements"/> for permanent business rules (plan features, user permissions, tier limits)</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class PlanEntitlements : Entitlements
/// {
///     /// &lt;summary&gt;Whether this plan can export data.&lt;/summary&gt;
///     public Entitlement&lt;bool&gt; CanExport { get; }
///
///     /// &lt;summary&gt;Maximum allowed team members.&lt;/summary&gt;
///     public Entitlement&lt;int&gt; MaxUsers { get; }
///
///     public PlanEntitlements(IReactiveConfig&lt;PlanConfig&gt; config)
///     {
///         CanExport = () => config.CurrentValue.Tier != "free";
///         MaxUsers  = () => config.CurrentValue.UserLimit;
///     }
/// }
/// </code>
/// </example>
public abstract class Entitlements
{
}
