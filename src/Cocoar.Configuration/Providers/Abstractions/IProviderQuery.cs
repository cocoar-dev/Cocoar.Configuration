namespace Cocoar.Configuration.Providers.Abstractions;

public interface IProviderQuery
{
    // Optional: wrap the provider result under a property path in the target config
    string? TargetPath { get; }
}
