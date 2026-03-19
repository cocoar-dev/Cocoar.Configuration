using Cocoar.Configuration.Configure;
using Cocoar.Configuration.DI.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI.Extensions;

public static class ConcreteTypeSetupExtensions
{

    public static ConcreteTypeSetup<T> RegisterAs<T>(this ConcreteTypeSetup<T> builder,
        ServiceLifetime serviceLifetime, object? key = null)
        where T : class
    {
        SetupDefinition.GetComposer(builder)
            .Add(new ServiceLifetimeCapability<SetupDefinition>(serviceLifetime, key));
        return builder;
    }

    public static ConcreteTypeSetup<T> AsSingleton<T>(this ConcreteTypeSetup<T> builder, object? key = null)
        where T : class =>
        builder.RegisterAs(ServiceLifetime.Singleton, key);

    public static ConcreteTypeSetup<T> AsScoped<T>(this ConcreteTypeSetup<T> builder, object? key = null)
        where T : class =>
        builder.RegisterAs(ServiceLifetime.Scoped, key);

    public static ConcreteTypeSetup<T> AsTransient<T>(this ConcreteTypeSetup<T> builder, object? key = null)
        where T : class =>
        builder.RegisterAs(ServiceLifetime.Transient, key);

    public static ConcreteTypeSetup<T> DisableAutoRegistration<T>(this ConcreteTypeSetup<T> builder)
        where T : class
    {
        SetupDefinition.GetComposer(builder)
            .Add(new DisableAutoRegistrationCapability<SetupDefinition>());
        return builder;
    }

}
