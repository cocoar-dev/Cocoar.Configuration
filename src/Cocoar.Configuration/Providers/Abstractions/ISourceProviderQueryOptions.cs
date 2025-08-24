namespace Cocoar.Configuration.Providers.Abstractions;

public interface ISourceProviderQueryOptions
{
    // Optional: wrap the provider result under a property path in the target config
    string? WrapperPath { get; }
}
