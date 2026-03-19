using System.Linq.Expressions;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Fluent builder for attaching <see cref="IContextResolver{TRequest,TContext}"/> implementations
/// to a specific flag or entitlement class.
/// <para>
/// Obtained from <see cref="ResolverBuilder.For{TFlags}"/>. Resolvers registered here participate
/// in the three-level cascade used by the REST evaluation endpoints:
/// property-level -> class-level -> global.
/// </para>
/// </summary>
/// <typeparam name="T">The flag or entitlement class being configured.</typeparam>
public sealed class ClassResolverBuilder<T> where T : class
{
    private readonly List<ContextResolverRegistration> _resolvers = [];

    /// <summary>
    /// Registers a class-level context resolver. Applies to all contextual properties on
    /// <typeparamref name="T"/> whose context type matches <typeparamref name="TResolver"/>'s
    /// <c>TContext</c>, unless a property-level resolver takes precedence.
    /// </summary>
    /// <typeparam name="TResolver">
    /// A type that implements <see cref="IContextResolver{TRequest,TContext}"/>.
    /// </typeparam>
    public ClassResolverBuilder<T> Use<TResolver>()
        where TResolver : class
    {
        var (requestType, contextType) = ResolverBuilder.ExtractResolverTypes(typeof(TResolver));
        _resolvers.Add(new ContextResolverRegistration(typeof(TResolver), requestType, contextType, null));
        return this;
    }

    /// <summary>
    /// Selects a property for property-level resolver configuration.
    /// </summary>
    /// <param name="propertySelector">
    /// A property access expression identifying which flag property this resolver covers,
    /// e.g. <c>f => f.BetaCheckout</c>.
    /// </param>
    /// <returns>A <see cref="PropertyResolverBuilder{T}"/> for attaching a resolver to this property.</returns>
    public PropertyResolverBuilder<T> ForProperty(Expression<Func<T, object?>> propertySelector)
    {
        var propertyName = ExtractPropertyName(propertySelector);
        return new PropertyResolverBuilder<T>(this, _resolvers, propertyName);
    }

    internal IReadOnlyList<ContextResolverRegistration> Build() => _resolvers.AsReadOnly();

    private static string ExtractPropertyName(Expression<Func<T, object?>> propertySelector)
    {
        var body = propertySelector.Body;

        // Handle boxing (e.g. value types or explicit casts to object?)
        if (body is UnaryExpression { NodeType: ExpressionType.Convert, Operand: MemberExpression boxed })
            return boxed.Member.Name;

        if (body is MemberExpression direct)
            return direct.Member.Name;

        throw new ArgumentException(
            "Expression must be a direct property access (e.g. f => f.MyFlag).",
            nameof(propertySelector));
    }
}

/// <summary>
/// Fluent builder for attaching a context resolver to a specific property.
/// </summary>
/// <typeparam name="T">The flag or entitlement class that owns the property.</typeparam>
public sealed class PropertyResolverBuilder<T> where T : class
{
    private readonly ClassResolverBuilder<T> _parent;
    private readonly List<ContextResolverRegistration> _resolvers;
    private readonly string _propertyName;

    internal PropertyResolverBuilder(
        ClassResolverBuilder<T> parent,
        List<ContextResolverRegistration> resolvers,
        string propertyName)
    {
        _parent = parent;
        _resolvers = resolvers;
        _propertyName = propertyName;
    }

    /// <summary>
    /// Registers a property-level context resolver for the selected property.
    /// Takes precedence over class-level and global resolvers.
    /// </summary>
    /// <typeparam name="TResolver">
    /// A type that implements <see cref="IContextResolver{TRequest,TContext}"/>.
    /// </typeparam>
    public ClassResolverBuilder<T> Use<TResolver>()
        where TResolver : class
    {
        var (requestType, contextType) = ResolverBuilder.ExtractResolverTypes(typeof(TResolver));
        _resolvers.Add(new ContextResolverRegistration(typeof(TResolver), requestType, contextType, _propertyName));
        return _parent;
    }
}
