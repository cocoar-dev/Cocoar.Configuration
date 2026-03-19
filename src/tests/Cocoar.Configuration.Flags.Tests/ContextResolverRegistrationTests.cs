using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.Flags.Tests;

/// <summary>
/// Tests for the three-level resolver cascade (property -> class -> global)
/// applied during UseFeatureFlags/ApplyFeatureFlags.
/// These tests use the internal ApplyFeatureFlags method directly to test
/// cascade behavior without depending on the DI package's resolver builder.
/// </summary>
public class ContextResolverRegistrationTests
{
    // ──────────────────────────────────────────────
    // Cascade: property -> class -> global
    // Tested via FlagsSetupData.EvaluationEntries
    // ──────────────────────────────────────────────

    [Fact]
    public void Cascade_PropertyLevel_WinsOverClassAndGlobal()
    {
        var flagReg = new FlagRegistration(MakeDescriptor<TestContextFlags>())
        {
            Resolvers = [
                MakeResolverRegistration<PropertyResolver>("ContextFeatureFlag"),
                MakeResolverRegistration<ClassResolver>(null)
            ]
        };

        var globalResolvers = new List<ContextResolverRegistration>
        {
            MakeResolverRegistration<GlobalResolver>(null)
        };

        using var manager = CreateWithResolvers([flagReg], globalResolvers);

        var entry = manager.FlagsSetup!.EvaluationEntries["TestContextFlags/ContextFeatureFlag"];
        Assert.Equal(typeof(PropertyResolver), entry.Resolver.ResolverType);
    }

    [Fact]
    public void Cascade_ClassLevel_WinsOverGlobal_WhenNoPropertyResolver()
    {
        var flagReg = new FlagRegistration(MakeDescriptor<TestContextFlags>())
        {
            Resolvers = [MakeResolverRegistration<ClassResolver>(null)]
        };

        var globalResolvers = new List<ContextResolverRegistration>
        {
            MakeResolverRegistration<GlobalResolver>(null)
        };

        using var manager = CreateWithResolvers([flagReg], globalResolvers);

        var entry = manager.FlagsSetup!.EvaluationEntries["TestContextFlags/ContextFeatureFlag"];
        Assert.Equal(typeof(ClassResolver), entry.Resolver.ResolverType);
    }

    [Fact]
    public void Cascade_GlobalFallback_UsedWhenNoPropertyOrClassResolver()
    {
        var flagReg = new FlagRegistration(MakeDescriptor<TestContextFlags>());

        var globalResolvers = new List<ContextResolverRegistration>
        {
            MakeResolverRegistration<GlobalResolver>(null)
        };

        using var manager = CreateWithResolvers([flagReg], globalResolvers);

        var entry = manager.FlagsSetup!.EvaluationEntries["TestContextFlags/ContextFeatureFlag"];
        Assert.Equal(typeof(GlobalResolver), entry.Resolver.ResolverType);
    }

    [Fact]
    public void Cascade_NoResolver_PropertyExcludedFromEvaluationEntries()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [])
            .UseFeatureFlags(flags => [flags.Register<TestContextFlags>()])); // no resolvers

        Assert.DoesNotContain("TestContextFlags/ContextFeatureFlag", manager.FlagsSetup!.EvaluationEntries.Keys);
    }

    [Fact]
    public void EvaluationEntries_NoContextFlag_NeverIncluded()
    {
        var flagReg = new FlagRegistration(MakeDescriptor<TestContextFlags>());
        var globalResolvers = new List<ContextResolverRegistration>
        {
            MakeResolverRegistration<GlobalResolver>(null)
        };

        using var manager = CreateWithResolvers([flagReg], globalResolvers);

        // NoContextFeatureFlag is FeatureFlag<bool>, not FeatureFlag<TContext, TResult> — must never appear
        Assert.DoesNotContain("TestContextFlags/NoContextFeatureFlag", manager.FlagsSetup!.EvaluationEntries.Keys);
    }

    [Fact]
    public void EvaluationEntries_CapturesCorrectContextAndFlagClassTypes()
    {
        var flagReg = new FlagRegistration(MakeDescriptor<TestContextFlags>());
        var globalResolvers = new List<ContextResolverRegistration>
        {
            MakeResolverRegistration<GlobalResolver>(null)
        };

        using var manager = CreateWithResolvers([flagReg], globalResolvers);

        var entry = manager.FlagsSetup!.EvaluationEntries["TestContextFlags/ContextFeatureFlag"];

        Assert.Equal(typeof(TestContextFlags), entry.FlagClassType);
        Assert.Equal(typeof(TestContext), entry.ContextType);
        Assert.Equal("ContextFeatureFlag", entry.Property.Name);
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates a ConfigManager with manually-specified flag registrations and resolvers,
    /// bypassing the public API to test cascade logic directly.
    /// </summary>
    private static ConfigManager CreateWithResolvers(
        FlagRegistration[] registrations,
        IReadOnlyList<ContextResolverRegistration> globalResolvers)
    {
        return ConfigManager.Create(c =>
        {
            c.UseConfiguration(rules => []);
            ConfigManagerBuilderExtensions.ApplyFeatureFlags(c, registrations, globalResolvers);
        });
    }

    private static FeatureFlagClassDescriptor MakeDescriptor<T>()
        => new(typeof(T), DateTimeOffset.MaxValue, []);

    private static ContextResolverRegistration MakeResolverRegistration<TResolver>(string? propertyName)
        where TResolver : class
    {
        var resolverType = typeof(TResolver);
        var iface = resolverType.GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IContextResolver<,>));

        return new ContextResolverRegistration(
            resolverType,
            iface.GenericTypeArguments[0],
            iface.GenericTypeArguments[1],
            propertyName);
    }

    // ─── Test types ────────────────────────────────

    internal record TestRequest(string Value = "");
    internal record TestContext(string Value = "");

    internal sealed class TestResolver : IContextResolver<TestRequest, TestContext>
    {
        public Task<TestContext> ResolveAsync(TestRequest request)
            => Task.FromResult(new TestContext("test_" + request.Value));
    }

    internal sealed class AltResolver : IContextResolver<TestRequest, TestContext>
    {
        public Task<TestContext> ResolveAsync(TestRequest request)
            => Task.FromResult(new TestContext("alt_" + request.Value));
    }

    internal sealed class PropertyResolver : IContextResolver<TestRequest, TestContext>
    {
        public Task<TestContext> ResolveAsync(TestRequest request)
            => Task.FromResult(new TestContext("property_" + request.Value));
    }

    internal sealed class ClassResolver : IContextResolver<TestRequest, TestContext>
    {
        public Task<TestContext> ResolveAsync(TestRequest request)
            => Task.FromResult(new TestContext("class_" + request.Value));
    }

    internal sealed class GlobalResolver : IContextResolver<TestRequest, TestContext>
    {
        public Task<TestContext> ResolveAsync(TestRequest request)
            => Task.FromResult(new TestContext("global_" + request.Value));
    }

    internal sealed class NotAResolver { }

    internal sealed class TestContextFlags : FeatureFlags
    {
        public override DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> NoContextFeatureFlag { get; }
        public FeatureFlag<TestContext, bool> ContextFeatureFlag { get; }
        public FeatureFlag<TestContext, bool> AnotherContextFeatureFlag { get; }

        public TestContextFlags()
        {
            NoContextFeatureFlag = () => true;
            ContextFeatureFlag = _ => true;
            AnotherContextFeatureFlag = _ => true;
        }
    }
}
