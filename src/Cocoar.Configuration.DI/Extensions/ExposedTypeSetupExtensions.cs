using System.Runtime.CompilerServices;
using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.DI.Capabilities;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI.Extensions;

public static class ExposedTypeSetupExtensions
{

    public static ExposedTypeSetup<T> RegisterAs<T>(this ExposedTypeSetup<T> builder, ServiceLifetime serviceLifetime, object? key = null)
        where T : class
    {
        SetupDefinition.GetComposer(builder)
            .Add(new ServiceLifetimeCapability<SetupDefinition>(serviceLifetime, key));
        return builder;
    }

    public static ExposedTypeSetup<T> AsSingleton<T>(this ExposedTypeSetup<T> builder, object? key = null)
        where T : class =>
        builder.RegisterAs(ServiceLifetime.Singleton, key);

    public static ExposedTypeSetup<T> AsScoped<T>(this ExposedTypeSetup<T> builder, object? key = null)
        where T : class =>
        builder.RegisterAs(ServiceLifetime.Scoped, key);

    public static ExposedTypeSetup<T> AsTransient<T>(this ExposedTypeSetup<T> builder, object? key = null)
        where T : class =>
        builder.RegisterAs(ServiceLifetime.Transient, key);

    public static ExposedTypeSetup<T> DisableAutoRegistration<T>(this ExposedTypeSetup<T> builder)
        where T : class {
        SetupDefinition.GetComposer(builder)
            .Add(new DisableAutoRegistrationCapability<SetupDefinition>());
        return builder;
    }
}
