namespace Cocoar.Configuration.Providers.Abstractions;

public interface ISourceProviderQueryOptions
{
    string? MemberPath { get; }
    string? MemberWrapper { get; }
}
