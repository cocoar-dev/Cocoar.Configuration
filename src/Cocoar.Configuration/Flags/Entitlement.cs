namespace Cocoar.Configuration.Flags;

/// <summary>
/// Delegate for an entitlement without context.
/// </summary>
/// <typeparam name="TResult">The return type of the entitlement.</typeparam>
public delegate TResult Entitlement<out TResult>();

/// <summary>
/// Delegate for a context-aware entitlement.
/// </summary>
/// <typeparam name="TContext">The context type required for evaluation.</typeparam>
/// <typeparam name="TResult">The return type of the entitlement.</typeparam>
public delegate TResult Entitlement<in TContext, out TResult>(TContext context);
