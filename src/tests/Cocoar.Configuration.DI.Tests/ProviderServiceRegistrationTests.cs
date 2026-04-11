using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.DI.Tests;

/// <summary>
/// Proves that the DI emitter discovers <see cref="IProviderServiceRegistration"/>
/// generically — without hardcoded knowledge of any specific provider.
/// A completely custom provider can contribute its own DI services.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Component", "DI")]
public class ProviderServiceRegistrationTests
{
    private sealed class AppConfig { public string? Name { get; set; } }

    // A custom service that the provider wants to register in DI
    public interface ICustomService<T> { string Greeting { get; } }

    private sealed class CustomServiceImpl<T>(string greeting) : ICustomService<T>
    {
        public string Greeting { get; } = greeting;
    }

    // Provider options that contribute a custom service via the generic interface
    private sealed class CustomProviderOptions(string greeting)
        : IProviderConfiguration, IProviderServiceRegistration
    {
        public string? GenerateProviderKey() => null;

        public IEnumerable<(Type ServiceType, object Implementation)> GetServiceRegistrations(Type concreteType)
        {
            var serviceType = typeof(ICustomService<>).MakeGenericType(concreteType);
            var implType = typeof(CustomServiceImpl<>).MakeGenericType(concreteType);
            yield return (serviceType, Activator.CreateInstance(implType, greeting)!);
        }
    }

    private sealed class CustomProviderQuery : IProviderQuery
    {
        public static readonly CustomProviderQuery Default = new();
    }

    // Minimal provider — returns empty JSON
    private sealed class CustomProvider(CustomProviderOptions options)
        : ConfigurationProvider<CustomProviderOptions, CustomProviderQuery>(options)
    {
        public override Task<byte[]> FetchConfigurationBytesAsync(
            CustomProviderQuery query, CancellationToken ct = default)
            => Task.FromResult("{}"u8.ToArray());

        public override IObservable<byte[]> ChangesAsBytes(CustomProviderQuery query)
            => new NeverObservable();

        private sealed class NeverObservable : IObservable<byte[]>
        {
            public IDisposable Subscribe(IObserver<byte[]> observer) => new Noop();
            private sealed class Noop : IDisposable { public void Dispose() { } }
        }
    }

    [Fact]
    public void CustomProvider_WithServiceRegistration_IsDiscoveredByEmitter()
    {
        var services = new ServiceCollection();
        var rule = new ConfigRule(
            typeof(CustomProvider),
            new CustomProviderOptions("Hello from custom provider"),
            CustomProviderQuery.Default,
            typeof(AppConfig));

        services.AddCocoarConfiguration(c => c.UseConfiguration([rule]));

        var sp = services.BuildServiceProvider();
        var customService = sp.GetService<ICustomService<AppConfig>>();

        Assert.NotNull(customService);
        Assert.Equal("Hello from custom provider", customService.Greeting);
    }

    [Fact]
    public void NoProviderServiceRegistration_NothingExtraRegistered()
    {
        // StaticJson doesn't implement IProviderServiceRegistration
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [
                rules.For<AppConfig>().FromStaticJson("""{"Name":"test"}""")
            ]));

        var sp = services.BuildServiceProvider();

        // ICustomService is not registered — only config + reactive
        Assert.Null(sp.GetService<ICustomService<AppConfig>>());
        Assert.NotNull(sp.GetService<AppConfig>());
    }

    [Fact]
    public void MultipleRules_LastRegistrationWins()
    {
        var services = new ServiceCollection();

        var rule1 = new ConfigRule(
            typeof(CustomProvider),
            new CustomProviderOptions("First"),
            CustomProviderQuery.Default,
            typeof(AppConfig));

        var rule2 = new ConfigRule(
            typeof(CustomProvider),
            new CustomProviderOptions("Second"),
            CustomProviderQuery.Default,
            typeof(AppConfig));

        services.AddCocoarConfiguration(c => c.UseConfiguration([rule1, rule2]));

        var sp = services.BuildServiceProvider();
        var customService = sp.GetRequiredService<ICustomService<AppConfig>>();

        Assert.Equal("Second", customService.Greeting);  // Last rule wins
    }
}
