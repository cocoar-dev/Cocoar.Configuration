namespace Cocoar.Configuration.Fluent;

/// <summary>
/// Base builder for provider-level operations.
/// Extension methods for FromFile, FromEnvironment, etc. target this type.
/// <para>
/// <see cref="TypedRuleBuilder{T}"/> inherits from this and adds aggregate operations
/// (Aggregate, FromFiles) that are not available inside aggregate lambdas.
/// </para>
/// </summary>
/// <typeparam name="T">The configuration type this rule will populate.</typeparam>
public class TypedProviderBuilder<T> where T : class
{
    internal TypedProviderBuilder() { }
}
