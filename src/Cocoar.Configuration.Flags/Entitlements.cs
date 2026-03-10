using Cocoar.Capabilities;

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
///     private readonly IReactiveConfig&lt;PlanConfig&gt; _config;
///
///     // Entitlements as delegates with metadata
///     public Entitlement&lt;bool&gt; CanExport { get; }
///     public Entitlement&lt;int&gt; MaxUsers { get; }
///     public Entitlement&lt;TenantContext, bool&gt; HasFeature { get; }
///
///     public PlanEntitlements(IReactiveConfig&lt;PlanConfig&gt; config)
///     {
///         _config = config;
///
///         CanExport = DefineEntitlement(
///             nameof(CanExport),
///             () =&gt; _config.CurrentValue.Tier != "free",
///             description: "Can export data"
///         );
///
///         MaxUsers = DefineEntitlement(
///             nameof(MaxUsers),
///             () =&gt; _config.CurrentValue.UserLimit,
///             description: "Maximum allowed users"
///         );
///
///         HasFeature = DefineEntitlement&lt;TenantContext, bool&gt;(
///             nameof(HasFeature),
///             ctx =&gt; _config.CurrentValue.EnabledFeatures.Contains(ctx.Feature),
///             description: "Check if tenant has specific feature"
///         );
///     }
/// }
///
/// // Usage
/// if (entitlements.CanExport()) { }
/// int max = entitlements.MaxUsers();
/// bool has = entitlements.HasFeature(tenantCtx);
///
/// // Access metadata
/// var meta = entitlements.GetMetadata(entitlements.CanExport);
/// </code>
/// </example>
public abstract class Entitlements : IDisposable
{
    private readonly CapabilityScope _scope = new(new CapabilityScopeOptions
    {
        UseCompositionRegistry = true
    });

    private readonly List<Delegate> _entitlements = new();

    /// <summary>
    /// Creates an entitlement delegate with metadata attached via Capabilities.
    /// </summary>
    /// <typeparam name="T">The return type of the entitlement.</typeparam>
    /// <param name="name">The name of the entitlement (use nameof(PropertyName)).</param>
    /// <param name="valueFactory">Function that returns the current entitlement value.</param>
    /// <param name="description">Optional description of what this entitlement controls.</param>
    /// <returns>The delegate with metadata attached.</returns>
    protected Entitlement<T> DefineEntitlement<T>(
        string name,
        Func<T> valueFactory,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        Entitlement<T> entitlement = () => valueFactory();

        _scope.Compose(entitlement)
            .WithPrimary(new EntitlementMetadata
            {
                Name = name,
                Description = description
            })
            .Build();

        _entitlements.Add(entitlement);
        return entitlement;
    }

    /// <summary>
    /// Creates a contextual entitlement delegate with metadata attached via Capabilities.
    /// </summary>
    /// <typeparam name="TContext">The context type required for evaluation.</typeparam>
    /// <typeparam name="TResult">The return type of the entitlement.</typeparam>
    /// <param name="name">The name of the entitlement (use nameof(PropertyName)).</param>
    /// <param name="evaluator">Function that evaluates the entitlement given a context.</param>
    /// <param name="description">Optional description of what this entitlement controls.</param>
    /// <returns>The delegate with metadata attached.</returns>
    protected Entitlement<TContext, TResult> DefineEntitlement<TContext, TResult>(
        string name,
        Func<TContext, TResult> evaluator,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(evaluator);

        Entitlement<TContext, TResult> entitlement = context => evaluator(context);

        _scope.Compose(entitlement)
            .WithPrimary(new EntitlementMetadata
            {
                Name = name,
                Description = description
            })
            .Build();

        _entitlements.Add(entitlement);
        return entitlement;
    }

    /// <summary>
    /// Gets the metadata for an entitlement delegate.
    /// </summary>
    /// <param name="entitlement">The entitlement delegate to get metadata for.</param>
    /// <returns>The entitlement metadata, or null if not found.</returns>
    public EntitlementMetadata? GetMetadata(Delegate entitlement)
    {
        ArgumentNullException.ThrowIfNull(entitlement);

        var composition = _scope.Compositions.GetOrDefault(entitlement);
        return composition?.GetPrimaryOrDefault() as EntitlementMetadata;
    }

    /// <summary>
    /// Gets all entitlement metadata registered in this class.
    /// </summary>
    /// <returns>Collection of all entitlement metadata.</returns>
    public IEnumerable<EntitlementMetadata> GetAllMetadata()
    {
        foreach (var entitlement in _entitlements)
        {
            var composition = _scope.Compositions.GetOrDefault(entitlement);
            if (composition?.GetPrimaryOrDefault() is EntitlementMetadata metadata)
            {
                yield return metadata;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _scope.Dispose();
        GC.SuppressFinalize(this);
    }
}
