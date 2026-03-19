namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Type-first rule builder - starts with For&lt;T&gt;() to ensure type safety.
/// Extension methods add provider-specific methods (FromFile, FromEnvironment, etc.).
/// </summary>
/// <typeparam name="T">The configuration type this rule will populate.</typeparam>
public sealed class TypedRuleBuilder<T> where T : class
{
    internal TypedRuleBuilder() { }
}
