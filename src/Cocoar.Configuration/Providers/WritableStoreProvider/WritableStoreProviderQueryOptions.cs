using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class WritableStoreProviderQueryOptions : IProviderQuery
{
    public static readonly WritableStoreProviderQueryOptions Default = new();
}
