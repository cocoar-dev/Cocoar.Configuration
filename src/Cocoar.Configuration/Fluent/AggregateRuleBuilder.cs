using Cocoar.Configuration.Core;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Builder for aggregate rules that group multiple sub-rules into a logical unit.
/// Supports <see cref="Required()"/>, <see cref="Named(string)"/>, and <see cref="When(Func{IConfigurationAccessor, bool})"/>.
/// </summary>
/// <typeparam name="T">The configuration type this aggregate populates.</typeparam>
public sealed class AggregateRuleBuilder<T> where T : class
{
    private readonly ConfigRule[] _subRules;
    private bool _required;
    private string? _name;
    private Func<IConfigurationAccessor, bool>? _useWhen;

    internal AggregateRuleBuilder(ConfigRule[] subRules)
    {
        if (subRules.Length == 0)
            throw new ArgumentException("Aggregate must contain at least one sub-rule.", nameof(subRules));
        _subRules = subRules;
    }

    /// <summary>
    /// Marks this aggregate as required. The merged result of all sub-rules must not be empty.
    /// </summary>
    public AggregateRuleBuilder<T> Required(bool value = true)
    {
        _required = value;
        return this;
    }

    /// <summary>
    /// Assigns a display name to this aggregate for health reporting and diagnostics.
    /// </summary>
    public AggregateRuleBuilder<T> Named(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty.", nameof(name));
        _name = name.Trim();
        return this;
    }

    /// <summary>
    /// Conditionally skips this entire aggregate based on current configuration state.
    /// </summary>
    public AggregateRuleBuilder<T> When(Func<IConfigurationAccessor, bool> predicate)
    {
        _useWhen = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    internal AggregateConfigRule Build()
    {
        var options = new ConfigRuleOptions(
            Required: _required,
            UseWhen: _useWhen,
            Name: _name);

        return new AggregateConfigRule(_subRules, typeof(T), options);
    }

    /// <summary>
    /// Implicit conversion to <see cref="ConfigRule"/> for use in collection expressions.
    /// </summary>
    public static implicit operator ConfigRule(AggregateRuleBuilder<T> builder)
        => builder.Build();
}
