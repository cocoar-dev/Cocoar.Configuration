using Cocoar.Capabilities;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Base class for feature flags. Feature flags are TEMPORARY and MUST have an expiration date.
/// <para>
/// Feature flags answer the question: "Does this code run?"
/// They are technical/operational in nature and owned by Engineering/Ops.
/// </para>
/// <para>
/// When <see cref="ExpiresAt"/> is reached, the flags continue to work but
/// <see cref="IsExpired"/> will return true. This is a code hygiene signal
/// indicating that the temporary code should be cleaned up.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>The Litmus Test:</b> "A Feature Flag without an expiration date is an Entitlement in disguise."
/// </para>
/// <para>
/// If you need permanent feature gating based on business rules (plans, tiers, permissions),
/// use <see cref="Entitlements"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class BillingFeatureFlags : FeatureFlags
/// {
///     public override DateTimeOffset ExpiresAt => new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
///
///     private readonly IReactiveConfig&lt;BillingConfig&gt; _config;
///
///     // Flags as delegates with metadata
///     public Flag&lt;bool&gt; NewFlowEnabled { get; }
///     public Flag&lt;int&gt; FlowVersion { get; }
///     public Flag&lt;UserContext, bool&gt; EnabledForUser { get; }
///
///     public BillingFeatureFlags(
///         IReactiveConfig&lt;BillingConfig&gt; config,
///         IFeatureFlagsRegistry? registry = null) : base(registry)
///     {
///         _config = config;
///
///         NewFlowEnabled = DefineFlag(
///             nameof(NewFlowEnabled),
///             () =&gt; _config.CurrentValue.NewFlowEnabled,
///             expiresAt: new(2025, 3, 1),
///             description: "Enables new billing flow"
///         );
///
///         FlowVersion = DefineFlag(
///             nameof(FlowVersion),
///             () =&gt; _config.CurrentValue.FlowVersion,
///             description: "Current billing flow version"
///         );
///
///         EnabledForUser = DefineFlag&lt;UserContext, bool&gt;(
///             nameof(EnabledForUser),
///             user =&gt; _config.CurrentValue.BetaUsers.Contains(user.Id),
///             description: "Per-user beta access"
///         );
///     }
/// }
///
/// // Usage
/// if (flags.NewFlowEnabled()) { }
/// int version = flags.FlowVersion();
/// bool enabled = flags.EnabledForUser(currentUser);
///
/// // Access metadata
/// var meta = flags.GetMetadata(flags.NewFlowEnabled);
/// </code>
/// </example>
public abstract class FeatureFlags : IDisposable
{
    private readonly CapabilityScope _scope = new(new CapabilityScopeOptions
    {
        UseCompositionRegistry = true
    });

    private readonly List<Delegate> _flags = new();

    /// <summary>
    /// Creates a new <see cref="FeatureFlags"/> instance, optionally registering with the provided registry.
    /// </summary>
    /// <param name="registry">Optional registry for auto-registration. If provided, this instance will be registered.</param>
    protected FeatureFlags(IFeatureFlagsRegistry? registry = null)
    {
        registry?.Register(this);
    }

    /// <summary>
    /// When should these flags be removed from code?
    /// After this date, the health API will report them as expired.
    /// The flags continue to work - this is a cleanup reminder.
    /// </summary>
    public abstract DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Is this feature flag class past its expiration?
    /// When true, the flags still work but the code should be cleaned up.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

    /// <summary>
    /// Creates a feature flag delegate with metadata attached via Capabilities.
    /// </summary>
    /// <typeparam name="T">The return type of the flag.</typeparam>
    /// <param name="name">The name of the flag (use nameof(PropertyName)).</param>
    /// <param name="valueFactory">Function that returns the current flag value.</param>
    /// <param name="expiresAt">Optional per-flag expiration (defaults to class-level ExpiresAt).</param>
    /// <param name="description">Optional description of what this flag does.</param>
    /// <returns>The delegate with metadata attached.</returns>
    protected Flag<T> DefineFlag<T>(
        string name,
        Func<T> valueFactory,
        DateTimeOffset? expiresAt = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        Flag<T> flag = () => valueFactory();

        _scope.Compose(flag)
            .WithPrimary(new FeatureFlagMetadata
            {
                Name = name,
                ExpiresAt = expiresAt ?? ExpiresAt,
                Description = description
            })
            .Build();

        _flags.Add(flag);
        return flag;
    }

    /// <summary>
    /// Creates a contextual feature flag delegate with metadata attached via Capabilities.
    /// </summary>
    /// <typeparam name="TContext">The context type required for evaluation.</typeparam>
    /// <typeparam name="TResult">The return type of the flag.</typeparam>
    /// <param name="name">The name of the flag (use nameof(PropertyName)).</param>
    /// <param name="evaluator">Function that evaluates the flag given a context.</param>
    /// <param name="expiresAt">Optional per-flag expiration (defaults to class-level ExpiresAt).</param>
    /// <param name="description">Optional description of what this flag does.</param>
    /// <returns>The delegate with metadata attached.</returns>
    protected Flag<TContext, TResult> DefineFlag<TContext, TResult>(
        string name,
        Func<TContext, TResult> evaluator,
        DateTimeOffset? expiresAt = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(evaluator);

        Flag<TContext, TResult> flag = context => evaluator(context);

        _scope.Compose(flag)
            .WithPrimary(new FeatureFlagMetadata
            {
                Name = name,
                ExpiresAt = expiresAt ?? ExpiresAt,
                Description = description
            })
            .Build();

        _flags.Add(flag);
        return flag;
    }

    /// <summary>
    /// Gets the metadata for a flag delegate.
    /// </summary>
    /// <param name="flag">The flag delegate to get metadata for.</param>
    /// <returns>The flag metadata, or null if not found.</returns>
    public FeatureFlagMetadata? GetMetadata(Delegate flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        var composition = _scope.Compositions.GetOrDefault(flag);
        return composition?.GetPrimaryOrDefault() as FeatureFlagMetadata;
    }

    /// <summary>
    /// Gets all flag metadata registered in this class.
    /// </summary>
    /// <returns>Collection of all flag metadata.</returns>
    public IEnumerable<FeatureFlagMetadata> GetAllMetadata()
    {
        foreach (var flag in _flags)
        {
            var composition = _scope.Compositions.GetOrDefault(flag);
            if (composition?.GetPrimaryOrDefault() is FeatureFlagMetadata metadata)
            {
                yield return metadata;
            }
        }
    }

    /// <summary>
    /// Gets all expired flag metadata.
    /// </summary>
    /// <returns>Collection of expired flag metadata.</returns>
    public IEnumerable<FeatureFlagMetadata> GetExpiredFlags()
    {
        return GetAllMetadata().Where(m => m.IsExpired);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _scope.Dispose();
        GC.SuppressFinalize(this);
    }
}
