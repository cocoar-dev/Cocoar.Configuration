using System.Net;
using System.Text;
using Cocoar.Configuration.Fluent;

using Cocoar.Configuration.HttpPolling;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Tests.Integration;

public class EndToEndDynamicDependencyTests
{
    public class BaseSettings
    {
        public Remote Remote { get; set; } = new();
    }
    public class Remote
    {
        public string Url { get; set; } = "/api/config1";
    }

    public class MyConfig
    {
        public int Value { get; set; }
    }

    [Fact]
    public async Task File_sets_url__Http_reads_url__Change_file_updates_url_and_recompute()
    {
        // Arrange temp dir and file with initial URL
        var dir = Path.Combine(Path.GetTempPath(), "cocoar_e2e_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "appsettings.json");
        await File.WriteAllTextAsync(file, "{ \"Remote\": { \"Url\": \"/api/config1\" } }");

        // HTTP handler that returns different payloads by path
        var handler = new PathSwitchHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["/api/config1"] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"Value\": 1 }", Encoding.UTF8, "application/json")
            },
            ["/api/config2"] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"Value\": 2 }", Encoding.UTF8, "application/json")
            }
        });

        var services = new ServiceCollection();
        services.AddCocoarConfiguration([
            // File rule provides BaseSettings (including Remote.Url)
          Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(file, TimeSpan.FromMilliseconds(80)))
              .For<BaseSettings>()
                 .Required()
                 .Build(),

            // HTTP rule depends on BaseSettings.Remote.Url for its query
            Rule.From
                .HttpPolling(cfg => new HttpPollingRuleOptions(
                    urlPathOrAbsolute: cfg.GetRequiredConfig<BaseSettings>().Remote.Url,
                    baseAddress: "https://example.com",
                    pollInterval: TimeSpan.FromMilliseconds(100),
                    handler: handler
                ))
                .When(() => true)
                .For<MyConfig>()
                .Required()
                .Build()
        ]);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<ConfigManager>();

        // Act + Assert 1: initial fetch uses /api/config1 -> Value = 1
        var first = manager.GetConfig<MyConfig>();
        Assert.NotNull(first);
        Assert.Equal(1, first!.Value);

        // Change the file to switch URL to /api/config2
        await File.WriteAllTextAsync(file, "{ \"Remote\": { \"Url\": \"/api/config2\" } }");

        // Wait until recompute yields Value = 2
        var sw = System.Diagnostics.Stopwatch.StartNew();
        MyConfig? second = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(80);
            second = manager.GetConfig<MyConfig>();
            if (second?.Value == 2) break;
        }
        Assert.Equal(2, second?.Value);

        // Cleanup
        Directory.Delete(dir, true);
    }

    private sealed class PathSwitchHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _map;
        private HttpResponseMessage? _last;
        public PathSwitchHandler(Dictionary<string, HttpResponseMessage> map) => _map = map;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null && _map.TryGetValue(request.RequestUri.PathAndQuery, out var resp))
            {
                _last = Clone(resp);
                return Task.FromResult(_last);
            }

            // Return last known response to keep config steady
            return Task.FromResult(_last ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }

        private static HttpResponseMessage Clone(HttpResponseMessage resp)
            => new(resp.StatusCode)
            {
                Content = resp.Content is null
                    ? null
                    : new StringContent(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult(), Encoding.UTF8, resp.Content.Headers.ContentType?.MediaType ?? "application/json")
            };
    }
}
