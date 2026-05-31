using System.Text;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;            // FromStaticJson
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.ServiceBacked.Tests;

// ============================================================================================================
// This whole file simulates a THIRD-PARTY provider package: a brand-new ConfigurationProvider<,> plus its own
// service-backed `(sp, a) => …` fluent overload — authored entirely against the PUBLIC surface (no
// InternalsVisibleTo). It proves a contributor can make their own provider service-backable; whether to do so is
// entirely the provider author's choice.
// ============================================================================================================

/// <summary>A trivial third-party provider that emits whatever JSON its options resolve.</summary>
public sealed class InlineProvider(InlineOptions options)
    : ConfigurationProvider<InlineOptions, InlineQuery>(options)
{
    public override Task<byte[]> FetchConfigurationBytesAsync(InlineQuery query, CancellationToken ct = default)
        => Task.FromResult(Encoding.UTF8.GetBytes(ProviderOptions.Json()));

    public override IObservable<byte[]> ChangesAsBytes(InlineQuery query) => NeverObservable.Instance;
}

public sealed class InlineOptions(Func<string> json) : IProviderConfiguration
{
    /// <summary>The JSON to emit — resolved lazily (so it can read a DI service at fetch time).</summary>
    public Func<string> Json { get; } = json;

    // Carries a closure over the IServiceProvider -> not shareable across rules (mirrors the HTTP/WritableStore rule).
    public string? GenerateProviderKey() => null;
}

public sealed class InlineQuery : IProviderQuery
{
    public static readonly InlineQuery Default = new();
}

internal sealed class NeverObservable : IObservable<byte[]>
{
    public static readonly NeverObservable Instance = new();

    public IDisposable Subscribe(IObserver<byte[]> observer) => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// The third-party service-backed authoring overload — uses only the public seam, via the ergonomic
/// <c>ServiceBacked((sp, a) =&gt; …)</c> helper (sp is a parameter invoked lazily; gating is automatic).
/// </summary>
public static class InlineProviderRulesExtensions
{
    public static ProviderRuleBuilder<InlineProvider, InlineOptions, InlineQuery> FromInline<T>(
        this ServiceBackedProviderBuilder<T> builder,
        Func<IServiceProvider, IConfigurationAccessor, string> jsonFactory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(jsonFactory);

        return builder.ServiceBacked<InlineProvider, InlineOptions, InlineQuery>(
            (sp, accessor) => new InlineOptions(() => jsonFactory(sp, accessor)),
            _ => InlineQuery.Default);
    }
}

public interface IInlineSource
{
    string GetJson();
}

internal sealed class InlineSource : IInlineSource
{
    public string GetJson() => """{ "Value": "from-third-party" }""";
}

[Trait("Category", "ServiceBacked")]
[Trait("Type", "Unit")]
public class ThirdPartyServiceBackedProviderTests
{
    [Fact]
    public async Task ThirdPartyProvider_AuthorsServiceBackedOverload_ViaPublicSeam()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInlineSource, InlineSource>();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }""") ])
            .UseServiceBackedConfiguration(rules =>
            [
                // The third-party (sp, a) => … overload resolves a DI service to load config.
                rules.For<RemoteConfig>().FromInline((sp, _) => sp.GetRequiredService<IInlineSource>().GetJson()),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        // Dormant before activation (the third-party overload gated itself via the public WithActivationGate).
        Assert.Equal("base", mgr.GetConfig<RemoteConfig>()!.Value);

        await sp.ActivateServiceBackedConfigurationAsync();

        // Activated: the third-party provider ran and resolved IInlineSource from the container.
        Assert.Equal("from-third-party", mgr.GetConfig<RemoteConfig>()!.Value);
    }

    [Fact]
    public void Context_ServiceProvider_ReadInAuthoringBody_ThrowsWithGuidance()
    {
        var services = new ServiceCollection();

        // Reading Context.ServiceProvider eagerly (in the authoring body, before the container exists) is a
        // mistake — it must fail loud with guidance, not silently yield null.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddCocoarConfiguration(c => c.UseServiceBackedConfiguration(rules =>
            {
                var builder = rules.For<RemoteConfig>();
                _ = builder.Context.ServiceProvider; // eager read — must throw
                return [];
            })));

        Assert.Contains("recompute time", ex.Message);
    }
}
