namespace Cocoar.Configuration.Flags;

/// <summary>
/// Hydrates a rich domain context object from a raw HTTP request payload.
/// <para>
/// Implement this interface to bridge the gap between what a TypeScript (or other) client
/// sends over HTTP (<typeparamref name="TRequest"/>) and the strongly-typed context that a
/// <see cref="FeatureFlag{TResult}"/> lambda receives (<typeparamref name="TContext"/>).
/// </para>
/// <para>
/// Implementations may perform any I/O — database lookups, service calls, cache reads —
/// and are resolved from DI at request time. They are registered as <c>Scoped</c> by
/// default, allowing resolvers to share per-request state (e.g. DbContext).
/// </para>
/// </summary>
/// <typeparam name="TRequest">The deserialized HTTP request body type.</typeparam>
/// <typeparam name="TContext">The domain context type expected by the flag evaluator.</typeparam>
/// <example>
/// <code>
/// public class UserByIdResolver : IContextResolver&lt;UserIdRequest, UserContext&gt;
/// {
///     private readonly IUserRepository _users;
///     public UserByIdResolver(IUserRepository users) => _users = users;
///
///     public async Task&lt;UserContext&gt; ResolveAsync(UserIdRequest request)
///     {
///         var user = await _users.GetByIdAsync(request.UserId);
///         return new UserContext(user.Id, user.Email, user.PlanTier);
///     }
/// }
/// </code>
/// </example>
public interface IContextResolver<TRequest, TContext>
{
    /// <summary>
    /// Resolves the domain context from the given HTTP request payload.
    /// </summary>
    Task<TContext> ResolveAsync(TRequest request);
}
