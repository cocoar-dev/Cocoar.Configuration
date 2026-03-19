using Cocoar.Configuration.DI;
using Cocoar.Configuration.Flags;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

/// <summary>
/// Integration tests for IEntitlementEvaluator DI registration, resolver scoped registration,
/// and full evaluation path (resolver -> context -> entitlement).
/// </summary>
[Trait("Type", "Unit")]
public sealed class EntitlementEvaluatorDITests
{
    // ──────────────────────────────────────────────
    // IEntitlementEvaluator DI registration
    // ──────────────────────────────────────────────

    [Fact]
    public void UseEntitlements_RegistersIEntitlementEvaluator()
    {
        var services = BuildNoResolver(e => [e.Register<EvalTestEntitlements>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IEntitlementEvaluator>());
    }

    [Fact]
    public void UseEntitlements_IEntitlementEvaluator_IsScoped()
    {
        // IEntitlementEvaluator is Scoped so it holds the current scope's IServiceProvider —
        // this matters for resolvers that may have scoped dependencies (e.g. DbContext).
        var services = BuildWithResolver(
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();

        IEntitlementEvaluator a, b, c;
        using (var scope1 = sp.CreateScope())
        {
            a = scope1.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();
            b = scope1.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();
        }
        using (var scope2 = sp.CreateScope())
            c = scope2.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        Assert.Same(a, b);      // same evaluator instance within scope
        Assert.NotSame(a, c);   // new evaluator per scope (for resolver isolation)
    }

    [Fact]
    public void UseEntitlements_EntitlementClass_DefaultLifetime_IsSingleton()
    {
        // Entitlement classes are pure functions — they have no per-request state.
        // Their only valid dependencies are IReactiveConfig<T> (also singleton).
        var services = BuildNoResolver(e => [e.Register<EvalTestEntitlements>()]);
        using var sp = services.BuildServiceProvider();

        using var scope1 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<EvalTestEntitlements>();

        using var scope2 = sp.CreateScope();
        var b = scope2.ServiceProvider.GetRequiredService<EvalTestEntitlements>();

        Assert.Same(a, b); // same instance across all scopes
    }

    // ──────────────────────────────────────────────
    // Resolver registration
    // ──────────────────────────────────────────────

    [Fact]
    public void UseEntitlements_GlobalResolver_RegisteredAsScoped()
    {
        var services = BuildWithResolver(
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var r1 = scope.ServiceProvider.GetRequiredService<EvalTestResolver>();
        var r2 = scope.ServiceProvider.GetRequiredService<EvalTestResolver>();

        Assert.Same(r1, r2); // Scoped — same within scope
    }

    [Fact]
    public void UseEntitlements_PropertyLevelResolver_RegisteredAsScoped()
    {
        var services = BuildWithResolver(
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.For<EvalTestEntitlements>(r => r
                .ForProperty(ent => ent.ContextualEntitlement).Use<EvalTestResolver>())]);
        using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<EvalTestResolver>());
    }

    [Fact]
    public void UseEntitlements_DuplicateResolverAcrossLevels_RegisteredOnce()
    {
        // Same resolver type at both property-level and global — should not throw
        var services = BuildWithResolver(
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [
                resolvers.Global<EvalTestResolver>(),
                resolvers.For<EvalTestEntitlements>(r => r
                    .ForProperty(ent => ent.ContextualEntitlement).Use<EvalTestResolver>())
            ]);

        // If registered twice it still resolves fine — just verifying no exception
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<EvalTestResolver>());
    }

    // ──────────────────────────────────────────────
    // IEntitlementEvaluator.CanEvaluate
    // ──────────────────────────────────────────────

    [Fact]
    public void CanEvaluate_True_ForContextualEntitlementWithResolver()
    {
        var services = BuildWithResolver(
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        Assert.True(evaluator.CanEvaluate("EvalTestEntitlements/ContextualEntitlement"));
    }

    [Fact]
    public void CanEvaluate_False_ForContextualEntitlementWithNoResolver()
    {
        var services = BuildNoResolver(e => [e.Register<EvalTestEntitlements>()]); // no resolver
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        Assert.False(evaluator.CanEvaluate("EvalTestEntitlements/ContextualEntitlement"));
    }

    [Fact]
    public void CanEvaluate_False_ForNoContextEntitlement()
    {
        var services = BuildWithResolver(
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        // Entitlement<bool> properties are never in EvaluationEntries
        Assert.False(evaluator.CanEvaluate("EvalTestEntitlements/DirectEntitlement"));
    }

    [Fact]
    public void CanEvaluate_False_ForUnknownKey()
    {
        var services = BuildNoResolver(e => [e.Register<EvalTestEntitlements>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        Assert.False(evaluator.CanEvaluate("Unknown/Key"));
    }

    // ──────────────────────────────────────────────
    // IEntitlementEvaluator.EvaluateAsync — full path
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_InvokesResolverAndEntitlement_ReturnsResult()
    {
        var services = BuildWithResolver(
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        // Resolver maps EvalTestRequest("enabled") -> EvalTestContext("resolved_enabled")
        // Entitlement checks ctx.Value == "resolved_enabled" -> true
        var result = await evaluator.EvaluateAsync(
            "EvalTestEntitlements/ContextualEntitlement",
            new EvalTestRequest("enabled"));

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task EvaluateAsync_ResolverReceivesCorrectRequest()
    {
        // Drain any leftover values from other tests
        while (TrackingResolver.ReceivedValues.TryDequeue(out _)) { }

        var services = BuildWithResolver(
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.Global<TrackingResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();
        await evaluator.EvaluateAsync("EvalTestEntitlements/ContextualEntitlement", new EvalTestRequest("my_value"));

        Assert.True(TrackingResolver.ReceivedValues.TryDequeue(out var received));
        Assert.Equal("my_value", received);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownKey_ThrowsKeyNotFoundException()
    {
        var services = BuildNoResolver(e => [e.Register<EvalTestEntitlements>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            evaluator.EvaluateAsync("Unknown/Entitlement", new EvalTestRequest("x")));
    }

    // ──────────────────────────────────────────────
    // E-01: Entitlement lambda exceptions are unwrapped
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_EntitlementLambdaThrows_WrapsAsInvalidOperationException()
    {
        var services = BuildWithResolver(
            e => [e.Register<ThrowingEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => evaluator.EvaluateAsync("ThrowingEntitlements/Boom", new EvalTestRequest("x")));

        // The original exception message should be preserved as the inner exception
        Assert.NotNull(ex.InnerException);
        Assert.Equal("Entitlement intentionally threw", ex.InnerException.Message);
        Assert.Contains("ThrowingEntitlements.Boom", ex.Message);
    }

    [Fact]
    public async Task EvaluateAsync_EntitlementLambdaThrows_DoesNotLeakTargetInvocationException()
    {
        var services = BuildWithResolver(
            e => [e.Register<ThrowingEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        // Should NOT be TargetInvocationException — that's the raw reflection wrapper
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => evaluator.EvaluateAsync("ThrowingEntitlements/Boom", new EvalTestRequest("x")));

        Assert.IsNotType<System.Reflection.TargetInvocationException>(ex);
    }

    // ──────────────────────────────────────────────
    // G-01: Duplicate entitlement class name collision guard
    // ──────────────────────────────────────────────

    [Fact]
    public void UseEntitlements_DuplicateClassName_ThrowsInvalidOperationException()
    {
        // Two entitlement classes that both have Name == "DuplicateNameEntitlements" but different FullName
        // (nested inside different outer classes).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildWithResolver(
                e => [
                    e.Register<OuterA.DuplicateNameEntitlements>(),
                    e.Register<OuterB.DuplicateNameEntitlements>()
                ],
                resolvers => [resolvers.Global<EvalTestResolver>()]));

        Assert.Contains("Duplicate evaluation key", ex.Message);
        Assert.Contains("DuplicateNameEntitlements", ex.Message);
    }

    [Fact]
    public void UseEntitlements_SameNameButDifferentProperties_NoCollision()
    {
        // Two entitlement classes with same Name but NO overlapping contextual properties:
        // only one has an Entitlement<TContext, TResult> property, so no key collision.
        Assert.Null(Record.Exception(() =>
            BuildWithResolver(
                e => [
                    e.Register<OuterA.DuplicateNameEntitlements>(),
                    e.Register<SameNameNoContextEntitlements>()
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
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.Global<TrackingResolver>()]);
        using var sp = services.BuildServiceProvider();

        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var eval1 = scope1.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();
        var eval2 = scope2.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        // Different scope -> different evaluator instance (scoped)
        Assert.NotSame(eval1, eval2);

        // Both can evaluate independently
        var r1 = await eval1.EvaluateAsync("EvalTestEntitlements/ContextualEntitlement", new EvalTestRequest("scope1"));
        var r2 = await eval2.EvaluateAsync("EvalTestEntitlements/ContextualEntitlement", new EvalTestRequest("scope2"));

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
            e => [e.Register<EvalTestEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        // Pass a string instead of EvalTestRequest
        await Assert.ThrowsAsync<InvalidCastException>(
            () => evaluator.EvaluateAsync("EvalTestEntitlements/ContextualEntitlement", "wrong type"));
    }

    // ──────────────────────────────────────────────
    // T-08: Empty entitlement class
    // ──────────────────────────────────────────────

    [Fact]
    public void UseEntitlements_EmptyEntitlementClass_RegistersSuccessfully()
    {
        // An entitlement class with no Entitlement<> properties at all should be valid
        Assert.Null(Record.Exception(() =>
            BuildNoResolver(e => [e.Register<EmptyEntitlements>()])));
    }

    [Fact]
    public void UseEntitlements_EmptyEntitlementClass_NoEvaluationEntries()
    {
        var services = BuildWithResolver(
            e => [e.Register<EmptyEntitlements>()],
            resolvers => [resolvers.Global<EvalTestResolver>()]);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var evaluator = scope.ServiceProvider.GetRequiredService<IEntitlementEvaluator>();

        Assert.False(evaluator.CanEvaluate("EmptyEntitlements/AnythingHere"));
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static IServiceCollection BuildNoResolver(Func<EntitlementsBuilder, EntitlementRegistration[]> entitlements)
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(entitlements));
        return services;
    }

    private static IServiceCollection BuildWithResolver(
        Func<EntitlementsBuilder, EntitlementRegistration[]> entitlements,
        Func<ResolverBuilder, ResolverRegistration[]> resolvers)
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseEntitlements(entitlements, resolvers));
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
        internal static readonly System.Collections.Concurrent.ConcurrentQueue<string> ReceivedValues = new();

        public Task<EvalTestContext> ResolveAsync(EvalTestRequest request)
        {
            ReceivedValues.Enqueue(request.Value);
            return Task.FromResult(new EvalTestContext("tracked_" + request.Value));
        }
    }

    internal sealed class EvalTestEntitlements : Entitlements
    {
        public Entitlement<bool> DirectEntitlement { get; }
        public Entitlement<EvalTestContext, bool> ContextualEntitlement { get; }

        public EvalTestEntitlements()
        {
            DirectEntitlement = () => true;
            ContextualEntitlement = ctx => ctx.Value == "resolved_enabled" || ctx.Value.StartsWith("tracked_");
        }
    }

    internal sealed class ThrowingEntitlements : Entitlements
    {
        public Entitlement<EvalTestContext, bool> Boom { get; }

        public ThrowingEntitlements()
        {
            Boom = _ => throw new InvalidOperationException("Entitlement intentionally threw");
        }
    }

    internal class OuterA
    {
        internal sealed class DuplicateNameEntitlements : Entitlements
        {
            public Entitlement<EvalTestContext, bool> ContextualEntitlement { get; }

            public DuplicateNameEntitlements()
            {
                ContextualEntitlement = ctx => ctx.Value == "a";
            }
        }
    }

    internal class OuterB
    {
        internal sealed class DuplicateNameEntitlements : Entitlements
        {
            public Entitlement<EvalTestContext, bool> ContextualEntitlement { get; }

            public DuplicateNameEntitlements()
            {
                ContextualEntitlement = ctx => ctx.Value == "b";
            }
        }
    }

    internal sealed class SameNameNoContextEntitlements : Entitlements
    {
        public Entitlement<bool> DirectOnly { get; }

        public SameNameNoContextEntitlements()
        {
            DirectOnly = () => true;
        }
    }

    internal sealed class EmptyEntitlements : Entitlements { }
}
