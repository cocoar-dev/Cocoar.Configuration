using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Fluent;

public abstract class RuleBuilderBase<TBuilder>
    where TBuilder : RuleBuilderBase<TBuilder>
{
    protected bool IsRequired { get; set; }

    /// <summary>
    /// Static marker set by <see cref="TenantScoped"/>. Distinct from the <see cref="UseWhen"/> predicate (which
    /// drives runtime skip): the DI planner and analyzers read this to exclude purely tenant-scoped types from
    /// the global injection plan (ADR-005 §5) without having to evaluate the predicate.
    /// </summary>
    protected bool IsTenantScoped { get; set; }

    protected Func<IConfigurationAccessor, bool>? UseWhen { get; set; }
    protected Type? ConcreteType { get; set; }
    protected string? MountPath { get; set; }
    protected string? SelectPath { get; set; }
    protected string? Name { get; set; }

    /// <summary>
    /// Marks the rule as required. If a required rule fails to load or deserialize,
    /// the entire recompute is rolled back and the previous configuration snapshot is retained.
    /// </summary>
    /// <param name="value">True to mark the rule as required (default); false to revert to optional.</param>
    /// <returns>This builder for chaining.</returns>
    public TBuilder Required(bool value = true)
    {
        IsRequired = value;
        return (TBuilder)this;
    }

    /// <summary>
    /// Conditionally executes this rule based on a predicate evaluated against the current configuration state.
    /// If the predicate returns false, the rule is skipped during recompute.
    /// </summary>
    /// <param name="predicate">A function that receives the current <see cref="IConfigurationAccessor"/> and returns true if the rule should run.</param>
    /// <returns>This builder for chaining.</returns>
    public TBuilder When(Func<IConfigurationAccessor, bool> predicate)
    {
        UseWhen = predicate;
        return (TBuilder)this;
    }

    /// <summary>
    /// Marks this rule as <b>tenant-scoped</b>: it runs only when the configuration is resolved for a tenant
    /// (<see cref="IConfigurationAccessor.Tenant"/> is present) and is skipped in the global, tenant-agnostic
    /// pipeline. Shorthand for <c>.When(a =&gt; !string.IsNullOrWhiteSpace(a.Tenant))</c>, composed (AND) with any
    /// existing <see cref="When"/> predicate. Use together with a tenant-varying factory, e.g.
    /// <c>.FromFile(a =&gt; $"db.{a.Tenant}.json").TenantScoped()</c>.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public TBuilder TenantScoped()
    {
        IsTenantScoped = true;
        var existing = UseWhen;
        UseWhen = existing is null
            ? static a => !string.IsNullOrWhiteSpace(a.Tenant)
            : a => existing(a) && !string.IsNullOrWhiteSpace(a.Tenant);
        return (TBuilder)this;
    }

    /// <summary>
    /// Sets the JSON mount path used when merging this rule's output into the target configuration type.
    /// Values from this rule are nested under the given path before deserialization.
    /// </summary>
    /// <param name="mountPath">The dot-notation path to mount values under (e.g., "Database" or "Feature.Flags").</param>
    /// <returns>This builder for chaining.</returns>
    public TBuilder MountAt(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
        {
            throw new ArgumentException("mountPath cannot be null/empty", nameof(mountPath));
        }

        MountPath = mountPath.Trim();
        return (TBuilder)this;
    }

    /// <summary>
    /// Sets the JSON selection path used to extract a sub-document from the provider's output
    /// before it is merged into the target configuration type.
    /// </summary>
    /// <param name="selectPath">The dot-notation path of the JSON property to select (e.g., "ConnectionStrings.Default").</param>
    /// <returns>This builder for chaining.</returns>
    public TBuilder Select(string selectPath)
    {
        if (string.IsNullOrWhiteSpace(selectPath))
        {
            throw new ArgumentException("selectPath cannot be null/empty", nameof(selectPath));
        }

        SelectPath = selectPath.Trim();
        return (TBuilder)this;
    }

    /// <summary>
    /// Assigns a display name to this rule for use in health monitoring and diagnostic output.
    /// </summary>
    /// <param name="name">A short, descriptive name for the rule (e.g., "BaseSettings", "ProductionOverride").</param>
    /// <returns>This builder for chaining.</returns>
    public TBuilder Named(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("name cannot be null/empty", nameof(name));
        }

        Name = name.Trim();
        return (TBuilder)this;
    }

    protected void SetConcreteType<T>()
    {
        ConcreteType = typeof(T);
    }

    protected Type BuildRegistration()
    {
        if (ConcreteType is null)
        {
            throw new InvalidOperationException(
                "Missing .For<YourConfigType>() call. Every rule needs to know which config class to populate.");
        }

        return ConcreteType;
    }
}
