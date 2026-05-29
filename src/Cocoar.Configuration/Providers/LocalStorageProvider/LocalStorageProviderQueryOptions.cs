using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class LocalStorageProviderQueryOptions : IProviderQuery
{
    public static readonly LocalStorageProviderQueryOptions Default = new();
}
