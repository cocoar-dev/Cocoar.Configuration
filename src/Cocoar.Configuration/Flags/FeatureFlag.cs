namespace Cocoar.Configuration.Flags;

/// <summary>
/// Delegate for a feature flag without context.
/// </summary>
/// <typeparam name="TResult">The return type of the flag.</typeparam>
public delegate TResult FeatureFlag<out TResult>();

/// <summary>
/// Delegate for a context-aware feature flag.
/// </summary>
/// <typeparam name="TContext">The context type required for evaluation.</typeparam>
/// <typeparam name="TResult">The return type of the flag.</typeparam>
public delegate TResult FeatureFlag<in TContext, out TResult>(TContext context);
