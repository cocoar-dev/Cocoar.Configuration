namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Type-first rule builder - starts with For&lt;T&gt;() to ensure type safety.
/// Inherits provider methods from <see cref="TypedProviderBuilder{T}"/> and adds
/// aggregate operations (Aggregate, FromFiles) that are not available inside aggregate lambdas.
/// </summary>
/// <typeparam name="T">The configuration type this rule will populate.</typeparam>
public sealed class TypedRuleBuilder<T> : TypedProviderBuilder<T> where T : class
{
    internal TypedRuleBuilder() { }
}
