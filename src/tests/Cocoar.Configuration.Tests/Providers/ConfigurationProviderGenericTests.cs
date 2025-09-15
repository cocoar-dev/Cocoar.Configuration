using System.Reactive.Linq;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Xunit;

namespace Cocoar.Configuration.Tests;

public class ConfigurationProviderGenericTests
{
    private sealed class TestProviderOptions : IProviderConfiguration
    {
        public string Key { get; }
        public TestProviderOptions(string key) => Key = key;
    }

    private sealed class TestProviderQuery : IProviderQuery
    {
        public string Id { get; }
        public TestProviderQuery(string id) => Id = id;
    }

    private sealed class WrongQueryType : IProviderQuery
    {
    }

    private sealed class TestProvider : ConfigurationProvider<TestProviderOptions, TestProviderQuery>
    {
        public TestProvider(TestProviderOptions options) : base(options)
        {
        }

        public override Task<JsonElement> FetchConfigurationAsync(TestProviderQuery query,
            CancellationToken ct = default)
        {
            using var doc = JsonDocument.Parse("{}");
            return Task.FromResult(doc.RootElement.Clone());
        }

        public override IObservable<JsonElement> Changes(TestProviderQuery query)
            => Observable.Empty<JsonElement>();
    }

    [Fact]
    public async Task FetchConfigurationAsync_ThrowsArgumentException_WhenQueryIsWrongType()
    {
        var provider = new TestProvider(new TestProviderOptions("test"));
        var wrongQuery = new WrongQueryType();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.FetchConfigurationAsync(wrongQuery, CancellationToken.None));

        Assert.Equal("query", ex.ParamName);
        Assert.Contains("Expected query of type", ex.Message);
        Assert.Contains("TestProviderQuery", ex.Message);
        Assert.Contains("WrongQueryType", ex.Message);
    }

    [Fact]
    public void Changes_ThrowsArgumentException_WhenQueryIsWrongType()
    {
        var provider = new TestProvider(new TestProviderOptions("test"));
        var wrongQuery = new WrongQueryType();

        var ex = Assert.Throws<ArgumentException>(() => provider.Changes(wrongQuery));

        Assert.Equal("query", ex.ParamName);
        Assert.Contains("Expected query of type", ex.Message);
        Assert.Contains("TestProviderQuery", ex.Message);
        Assert.Contains("WrongQueryType", ex.Message);
    }
}
