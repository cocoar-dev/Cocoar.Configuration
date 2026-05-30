using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Testing;

namespace Cocoar.Configuration.Fluent;

/// <summary>
/// The builder for service-backed (Layer-2, ADR-006) rules, returned by
/// <see cref="ServiceBackedRulesBuilder.For{T}"/>. It inherits every provider method of
/// <see cref="TypedProviderBuilder{T}"/> (so a non-DI rule like <c>FromFile</c> can still be placed in Layer 2)
/// and additionally carries the <see cref="ServiceBackedRuleContext"/> that <c>(sp, a) =&gt; …</c> overloads read.
/// <para>
/// Because those overloads (<c>FromStore</c>, <c>FromHttp((sp,a)=&gt;…)</c>, and any third-party one) target
/// <b>this</b> type, using them inside the Layer-1 <c>UseConfiguration</c> — which yields a plain
/// <see cref="TypedProviderBuilder{T}"/> — is a <b>compile error</b>, not a runtime fault. A third-party provider
/// is made service-backable simply by authoring an extension method on this type (and giving its provider options
/// a slot for the resolved artifact); whether to do so is entirely the provider author's choice.
/// </para>
/// </summary>
/// <typeparam name="T">The configuration type this rule will populate.</typeparam>
public sealed class ServiceBackedProviderBuilder<T> : TypedProviderBuilder<T> where T : class
{
    internal ServiceBackedProviderBuilder(ServiceBackedRuleContext context) => Context = context;

    /// <summary>
    /// The container hook a service-backed rule factory resolves application services from. Prefer
    /// <see cref="ServiceBacked{TProvider, TOptions, TQuery}"/> over reading this directly — it hands you the
    /// provider as a <c>(sp, accessor) =&gt; …</c> factory invoked at the right time. <see cref="ServiceBackedRuleContext.ServiceProvider"/>
    /// is only valid at recompute time and throws if read in the (eager) authoring body.
    /// </summary>
    public ServiceBackedRuleContext Context { get; }

    /// <summary>
    /// Builds a service-backed (Layer-2, ADR-006) provider rule from factories that receive the application
    /// <see cref="System.IServiceProvider"/>. This is the safe, ergonomic way to author a custom service-backed
    /// provider overload: <paramref name="optionsFactory"/> gets <c>sp</c> as a <b>parameter</b> invoked lazily at
    /// recompute time (so it can never be read too early), and the rule is gated automatically (dormant until the
    /// container is built). <c>sp</c> is the root provider — resolve singletons / factories and open short-lived
    /// units per read (§9).
    /// </summary>
    /// <example>
    /// <code>
    /// public static ProviderRuleBuilder&lt;MyProvider, MyOptions, MyQuery&gt; FromMyDb&lt;T&gt;(
    ///     this ServiceBackedProviderBuilder&lt;T&gt; builder,
    ///     Func&lt;IServiceProvider, IConfigurationAccessor, MyBackend&gt; backendFactory, string key) where T : class
    ///     =&gt; builder.ServiceBacked&lt;MyProvider, MyOptions, MyQuery&gt;(
    ///         (sp, a) =&gt; new MyOptions(backendFactory(sp, a)),
    ///         _ =&gt; new MyQuery(key));
    /// </code>
    /// </example>
    /// <param name="optionsFactory">Builds the provider options from the root <see cref="System.IServiceProvider"/>
    /// and the current <see cref="IConfigurationAccessor"/> (its <c>Tenant</c> is set in a tenant pipeline).</param>
    /// <param name="queryFactory">Builds the per-rule query options.</param>
    public ProviderRuleBuilder<TProvider, TOptions, TQuery> ServiceBacked<TProvider, TOptions, TQuery>(
        Func<IServiceProvider, IConfigurationAccessor, TOptions> optionsFactory,
        Func<IConfigurationAccessor, TQuery> queryFactory)
        where TProvider : ConfigurationProvider<TOptions, TQuery>
        where TOptions : IProviderConfiguration
        where TQuery : IProviderQuery
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);
        ArgumentNullException.ThrowIfNull(queryFactory);

        var context = Context;
        var rule = new ProviderRuleBuilder<TProvider, TOptions, TQuery>(
            accessor => optionsFactory(context.ServiceProvider, accessor),  // sp read here = recompute time, never eager
            queryFactory,
            typeof(T));

        return rule.WithActivationGate(_ => context.IsActive);              // dormant until the container is built
    }

    /// <summary>
    /// Service-backed (Layer-2, ADR-006) rule that derives the whole configuration object from a single DI
    /// service — Cocoar's equivalent of Microsoft's <c>services.Configure&lt;TDep&gt;((opts, dep) =&gt; …)</c> /
    /// an <c>IConfigureOptions&lt;T&gt;</c> with an injected dependency. <typeparamref name="TService"/> is resolved
    /// from the container at recompute time (after host start) and handed to <paramref name="projection"/>, which
    /// returns the config value. No custom provider needed; the rule is dormant until the container is built and is
    /// the natural target when migrating <c>Configure&lt;TDep&gt;</c>/<c>IConfigureOptions&lt;T&gt;</c>.
    /// <para>
    /// Synchronous / in-memory by nature (it snapshots once per recompute, no change detection). For I/O-bound
    /// sources (DB, HTTP) use an async provider instead — <c>FromStore</c>, <c>FromHttp((sp,a)=&gt;…)</c>, or a
    /// custom provider — rather than blocking here.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// rules.For&lt;AppSettings&gt;().FromService&lt;AppSettingsService&gt;(s =&gt; s.Settings)
    /// </code>
    /// </example>
    /// <typeparam name="TService">The DI service to resolve and project from.</typeparam>
    /// <param name="projection">Maps the resolved service to the configuration value.</param>
    public ProviderRuleBuilder<StaticJsonProvider, StaticJsonProviderOptions, StaticJsonProviderQueryOptions>
        FromService<TService>(Func<TService, T> projection)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(projection);

        return ServiceBacked<StaticJsonProvider, StaticJsonProviderOptions, StaticJsonProviderQueryOptions>(
            (sp, _) =>
            {
                // BCL IServiceProvider.GetService(Type) keeps the No-DI core free of a Microsoft.Extensions.DI dependency.
                if (sp.GetService(typeof(TService)) is not TService service)
                {
                    throw new InvalidOperationException(
                        $"Service '{typeof(TService).Name}' is not registered; cannot build configuration " +
                        $"'{typeof(T).Name}' via FromService<{typeof(TService).Name}>().");
                }

                var json = JsonSerializer.SerializeToElement(projection(service), CocoarTestConfiguration.Current?.SerializerOptions);
                return new StaticJsonProviderOptions(json);
            },
            _ => new StaticJsonProviderQueryOptions());
    }
}
