namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Captures the registration of an <see cref="IContextResolver{TRequest,TContext}"/> implementation,
/// including the scope at which it applies (property-level or class-level).
/// </summary>
/// <param name="ResolverType">
/// The concrete resolver type (e.g. <c>typeof(UserByIdResolver)</c>).
/// </param>
/// <param name="RequestType">
/// The HTTP request type resolved from <c>IContextResolver&lt;TRequest,TContext&gt;</c>'s first
/// generic argument.
/// </param>
/// <param name="ContextType">
/// The domain context type resolved from <c>IContextResolver&lt;TRequest,TContext&gt;</c>'s second
/// generic argument.
/// </param>
/// <param name="PropertyName">
/// When non-null, the registration applies only to the named property (property-level).
/// When null, it applies to all contextual flag properties whose context type matches
/// (class-level fallback).
/// </param>
internal sealed record ContextResolverRegistration(
    Type ResolverType,
    Type RequestType,
    Type ContextType,
    string? PropertyName);
