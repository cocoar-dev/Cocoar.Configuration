using Xunit;
using System.Text.Json;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Providers.Tests.CommandLine;

public class CommandLineParserDebugTests
{
    [Fact]
    public async Task DirectProviderCall_ParsesCorrectly()
    {
        var args = new[] { "--host=localhost", "--port=8080" };
        var options = new CommandLineProviderOptions();
        var provider = new CommandLineArgumentProvider(options);
        var queryOptions = new CommandLineProviderQueryOptions(args, null, null);
        
        var result = await provider.FetchConfigurationAsync(queryOptions);
        
        var jsonString = result.GetRawText();
        System.Console.WriteLine($"JSON Result: {jsonString}");
        
        Assert.True(result.TryGetProperty("host", out var hostProp) || result.TryGetProperty("Host", out hostProp));
        Assert.Equal("localhost", hostProp.GetString());
    }
}
