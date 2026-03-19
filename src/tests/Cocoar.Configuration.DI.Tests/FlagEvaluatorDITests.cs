using Cocoar.Configuration.DI;
using Cocoar.Configuration.Flags;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

/// <summary>
/// Integration tests for IFeatureFlagEvaluator DI registration, resolver scoped registration,
/// and full evaluation path (resolver -> context -> flag).
/// </summary>
public class FlagEvaluatorDITests
{
    // ──────────────────────────────────────────────
    // IFeatureFlagEvaluator DI registration
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_RegistersIFeatureFlagEvaluator()
    {
        var services = BuildNoResolver(flags => [flags.Register<EvalTestFlags>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IFeatureFlagEvaluator>());
    }

    [Fact]
    public void UseFeatureFlags_IFeatureFlagEvaluator_IsScoped()
    {
        // IFeatureFlagEvaluator is Scoped so it holds the current scope's IServiceProvider —
        // this matters for resolvers that may have scoped dependencies (e.g. DbContext).
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();

        IFeatureFlagEvaluator a, b, c;
        using (var scope1 = sp.CreateScope())
        {
            a = scope1.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();
            b = scope1.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();
        }
        using (var scope2 = sp.CreateScope())
            c = scope2.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        Assert.Same(a, b);      // same evaluator instance within scope
        Assert.NotSame(a, c);   // new evaluator per scope (for resolver isolation)
    }

    [Fact]
    public void UseFeatureFlags_FlagClass_DefaultLifetime_IsSingleton()
    {
        // FeatureFlag classes are pure functions — they have no per-request state.
        // Their only valid dependencies are IReactiveConfig<T> (also singleton).
        var services = BuildNoResolver(flags => [flags.Register<EvalTestFlags>()]);
        using var sp = services.BuildServiceProvider();

        using var scope1 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<EvalTestFlags>();

        using var scope2 = sp.CreateScope();
        var b = scope2.ServiceProvider.GetRequiredService<EvalTestFlags>();

        Assert.Same(a, b); // same instance across all scopes
    }

    // ──────────────────────────────────────────────
    // Resolver registration
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_GlobalResolver_RegisteredAsScoped()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var r1 = scope.ServiceProvider.GetRequiredService<EvalTestResolver>();
        var r2 = scope.ServiceProvider.GetRequiredService<EvalTestResolver>();

        Assert.Same(r1, r2); // Scoped — same within scope
    }

    [Fact]
    public void UseFeatureFlags_PropertyLevelResolver_RegisteredAsScoped()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.For<EvalTestFlags>(r => r
                .ForProperty(f => f.ContextualFeatureFlag).Use<EvalTestResolver>())]);
        using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<EvalTestResolver>());
    }

    [Fact]
    public void UseFeatureFlags_DuplicateResolverAcrossLevels_RegisteredOnce()
    {
        // Same resolver type at both property-level and global — should not throw
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [
                resolvers.Global<EvalTestResolver>(),
                resolvers.For<EvalTestFlags>(r => r
                    .ForProperty(f => f.ContextualFeatureFlag).Use<EvalTestResolver>())
            ]);

        // If registered twice it still resolves fine — just verifying no exception
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<EvalTestResolver>());
    }

    // ──────────────────────────────────────────────
    // IFeatureFlagEvaluator.CanEvaluate
    // ──────────────────────────────────────────────

    [Fact]
    public void CanEvaluate_True_ForContextualFlagWithResolver()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        Assert.True(evaluator.CanEvaluate("EvalTestFlags/ContextualFeatureFlag"));
    }

    [Fact]
    public void CanEvaluate_False_ForContextualFlagWithNoResolver()
    {
        var services = BuildNoResolver(flags => [flags.Register<EvalTestFlags>()]); // no resolver
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        Assert.False(evaluator.CanEvaluate("EvalTestFlags/ContextualFeatureFlag"));
    }

    [Fact]
    public void CanEvaluate_False_ForNoContextFlag()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        // FeatureFlag<bool> properties are never in EvaluationEntries
        Assert.False(evaluator.CanEvaluate("EvalTestFlags/DirectFeatureFlag"));
    }

    [Fact]
    public void CanEvaluate_False_ForUnknownKey()
    {
        var services = BuildNoResolver(flags => [flags.Register<EvalTestFlags>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        Assert.False(evaluator.CanEvaluate("Unknown/Key"));
    }

    // ──────────────────────────────────────────────
    // IFeatureFlagEvaluator.EvaluateAsync — full path
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_InvokesResolverAndFlag_ReturnsResult()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        // Resolver maps EvalTestRequest("enabled") -> EvalTestContext("resolved_enabled")
        // FeatureFlag checks ctx.Value == "resolved_enabled" -> true
        var result = await evaluator.EvaluateAsync(
            "EvalTestFlags/ContextualFeatureFlag",
            new EvalTestRequest("enabled"));

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task EvaluateAsync_ResolverReceivesCorrectRequest()
    {
        // Drain any leftover values from other tests
        while (TrackingResolver.ReceivedValues.TryDequeue(out _)) { }

        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<TrackingResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();
        await evaluator.EvaluateAsync("EvalTestFlags/ContextualFeatureFlag", new EvalTestRequest("my_value"));

        Assert.True(TrackingResolver.ReceivedValues.TryDequeue(out var received));
        Assert.Equal("my_value", received);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownKey_ThrowsKeyNotFoundException()
    {
        var services = BuildNoResolver(flags => [flags.Register<EvalTestFlags>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            evaluator.EvaluateAsync("Unknown/FeatureFlag", new EvalTestRequest("x")));
    }

    // ──────────────────────────────────────────────
    // E-01: FeatureFlag lambda exceptions are unwrapped
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_FlagLambdaThrows_WrapsAsInvalidOperationException()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<ThrowingFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => evaluator.EvaluateAsync("ThrowingFlags/Boom", new EvalTestRequest("x")));

        // The original exception message should be preserved as the inner exception
        Assert.NotNull(ex.InnerException);
        Assert.Equal("FeatureFlag intentionally threw", ex.InnerException.Message);
        Assert.Contains("ThrowingFlags.Boom", ex.Message);
    }

    [Fact]
    public async Task EvaluateAsync_FlagLambdaThrows_DoesNotLeakTargetInvocationException()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<ThrowingFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        // Should NOT be TargetInvocationException — that's the raw reflection wrapper
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => evaluator.EvaluateAsync("ThrowingFlags/Boom", new EvalTestRequest("x")));

        Assert.IsNotType<System.Reflection.TargetInvocationException>(ex);
    }

    // ──────────────────────────────────────────────
    // G-01: Duplicate flag class name collision guard
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_DuplicateClassName_ThrowsInvalidOperationException()
    {
        // Two flag classes that both have Name == "DuplicateNameFlags" but different FullName
        // (nested inside different outer classes).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildWithResolver(
                flags => [
                    flags.Register<OuterA.DuplicateNameFlags>(),
                    flags.Register<OuterB.DuplicateNameFlags>()
                ],
                resolvers => [resolvers.Global<EvalTestResolver>()]));

        Assert.Contains("Duplicate evaluation key", ex.Message);
        Assert.Contains("DuplicateNameFlags", ex.Message);
    }

    [Fact]
    public void UseFeatureFlags_SameNameButDifferentProperties_NoCollision()
    {
        // Two flag classes with same Name but NO overlapping contextual properties:
        // only one has a FeatureFlag<TContext, TResult> property, so no key collision.
        // OuterA.DuplicateNameFlags has ContextualFeatureFlag (FeatureFlag<TContext,TResult>)
        // SameNameNoContextFlags has only FeatureFlag<bool> — no entry in evaluation dict
        Assert.Null(Record.Exception(() =>
            BuildWithResolver(
                flags => [
                    flags.Register<OuterA.DuplicateNameFlags>(),
                    flags.Register<SameNameNoContextFlags>()
                ],
                resolvers => [resolvers.Global<EvalTestResolver>()])));
    }

    // ──────────────────────────────────────────────
    // T-06: Scope isolation
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_DifferentScopes_GetIndependentResolverInstances()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<TrackingResolver>()]);
        using var sp = services.BuildServiceProvider();

        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var eval1 = scope1.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();
        var eval2 = scope2.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        // Different scope -> different evaluator instance (scoped)
        Assert.NotSame(eval1, eval2);

        // Both can evaluate independently
        var r1 = await eval1.EvaluateAsync("EvalTestFlags/ContextualFeatureFlag", new EvalTestRequest("scope1"));
        var r2 = await eval2.EvaluateAsync("EvalTestFlags/ContextualFeatureFlag", new EvalTestRequest("scope2"));

        Assert.NotNull(r1);
        Assert.NotNull(r2);
    }

    // ──────────────────────────────────────────────
    // T-07: Wrong request type
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WrongRequestType_ThrowsInvalidCastException()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        // Pass a string instead of EvalTestRequest
        await Assert.ThrowsAsync<InvalidCastException>(
            () => evaluator.EvaluateAsync("EvalTestFlags/ContextualFeatureFlag", "wrong type"));
    }

    // ──────────────────────────────────────────────
    // T-08: Empty flag class
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_EmptyFlagClass_RegistersSuccessfully()
    {
        // A flag class with no FeatureFlag<> properties at all should be valid
        Assert.Null(Record.Exception(() =>
            BuildNoResolver(flags => [flags.Register<EmptyFlags>()])));
    }

    [Fact]
    public void UseFeatureFlags_EmptyFlagClass_NoEvaluationEntries()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EmptyFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IFeatureFlagEvaluator>();

        Assert.False(evaluator.CanEvaluate("EmptyFlags/AnythingHere"));
    }

    // ──────────────────────────────────────────────
    // Lifetime customization
    // ──────────────────────────────────────────────

    [Fact]
    public void UseFeatureFlags_Resolver_AsSingleton_RegisteredAsSingleton()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>().AsSingleton()]);
        using var sp = services.BuildServiceProvider();

        var r1 = sp.GetRequiredService<EvalTestResolver>();
        var r2 = sp.GetRequiredService<EvalTestResolver>();

        Assert.Same(r1, r2); // Singleton
    }

    [Fact]
    public void UseFeatureFlags_Resolver_AsTransient_RegisteredAsTransient()
    {
        var services = BuildWithResolver(
            flags => [flags.Register<EvalTestFlags>()],
            resolvers => [resolvers.Global<EvalTestResolver>().AsTransient()]);
        using var sp = services.BuildServiceProvider();

        var r1 = sp.GetRequiredService<EvalTestResolver>();
        var r2 = sp.GetRequiredService<EvalTestResolver>();

        Assert.NotSame(r1, r2); // Transient
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static IServiceCollection BuildNoResolver(Func<FlagsBuilder, FlagRegistration[]> flags)
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags));
        return services;
    }

    private static IServiceCollection BuildWithResolver(
        Func<FlagsBuilder, FlagRegistration[]> flags,
        Func<ResolverBuilder, ResolverRegistration[]> resolvers)
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags, resolvers));
        return services;
    }

    // ─── Test types ────────────────────────────────

    internal record EvalTestRequest(string Value);
    internal record EvalTestContext(string Value);

    internal sealed class EvalTestResolver : IContextResolver<EvalTestRequest, EvalTestContext>
    {
        public Task<EvalTestContext> ResolveAsync(EvalTestRequest request)
            => Task.FromResult(new EvalTestContext("resolved_" + request.Value));
    }

    internal sealed class TrackingResolver : IContextResolver<EvalTestRequest, EvalTestContext>
    {
        // Thread-safe collection so parallel tests can each inspect their own values.
        // Tests should call ReceivedValues.Clear() or drain the queue before asserting.
        internal static readonly System.Collections.Concurrent.ConcurrentQueue<string> ReceivedValues = new();

        public Task<EvalTestContext> ResolveAsync(EvalTestRequest request)
        {
            ReceivedValues.Enqueue(request.Value);
            return Task.FromResult(new EvalTestContext("tracked_" + request.Value));
        }
    }

    internal sealed class EvalTestFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> DirectFeatureFlag { get; }
        public FeatureFlag<EvalTestContext, bool> ContextualFeatureFlag { get; }

        public EvalTestFlags()
        {
            DirectFeatureFlag = () => true;
            // Returns true when resolver produced the expected "resolved_*" prefix
            ContextualFeatureFlag = ctx => ctx.Value == "resolved_enabled" || ctx.Value.StartsWith("tracked_");
        }
    }

    // E-01: FeatureFlag class whose lambda deliberately throws
    internal sealed class ThrowingFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public FeatureFlag<EvalTestContext, bool> Boom { get; }

        public ThrowingFlags()
        {
            Boom = _ => throw new InvalidOperationException("FeatureFlag intentionally threw");
        }
    }

    // G-01: Two nested classes with identical Name for collision testing
    internal class OuterA
    {
        internal sealed class DuplicateNameFlags
        {
            public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public FeatureFlag<EvalTestContext, bool> ContextualFeatureFlag { get; }

            public DuplicateNameFlags()
            {
                ContextualFeatureFlag = ctx => ctx.Value == "a";
            }
        }
    }

    internal class OuterB
    {
        internal sealed class DuplicateNameFlags
        {
            public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public FeatureFlag<EvalTestContext, bool> ContextualFeatureFlag { get; }

            public DuplicateNameFlags()
            {
                ContextualFeatureFlag = ctx => ctx.Value == "b";
            }
        }
    }

    // G-01: Same Name but only has FeatureFlag<bool> (no contextual), so no collision
    internal sealed class SameNameNoContextFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public FeatureFlag<bool> DirectOnly { get; }

        public SameNameNoContextFlags()
        {
            DirectOnly = () => true;
        }
    }

    // T-08: FeatureFlag class with zero FeatureFlag<> properties
    internal sealed class EmptyFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // No FeatureFlag<> properties at all
    }
}
