using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Providers.Abstractions;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.Health;

public sealed class ConfigManagerHealthRecoveryTests
{
    private sealed class FlakyStaticProvider : ConfigurationProvider<DummyProviderOptions, DummyProviderQuery>
    {
        private static int _calls;
        public FlakyStaticProvider(DummyProviderOptions options) : base(options) { }
        public override Task<byte[]> FetchConfigurationBytesAsync(DummyProviderQuery query, CancellationToken ct = default)
        {
            _calls++;
            if (_calls == 1)
            {
                throw new InvalidOperationException("Boom once");
            }

            var json = """{"Name":"Ok","Value":42}""";
            return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(json));
        }
        public override IObservable<byte[]> ChangesAsBytes(DummyProviderQuery query) => System.Reactive.Linq.Observable.Never<byte[]>();
    }

    private sealed class DummyProviderOptions : IProviderConfiguration
    {
        public string GenerateProviderKey() => "flaky";
    }

    private sealed class DummyProviderQuery : IProviderQuery
    {
        public string GenerateProviderKey() => "flaky-query";
    }

    [Fact]
    [Trait("Type","Unit")]
    [Trait("Provider","ConfigManager")]    
    public void OptionalFailure_ThenRecovery_ResetsFailureCount()
    {
        var rules = new []
        {
            new ConfigRule(typeof(FlakyStaticProvider), new DummyProviderOptions(), new DummyProviderQuery(), typeof(RecoveryConfig), new(Required: false))
        };

        var manager = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseProviderFactory((t, _) => (ConfigurationProvider)Activator.CreateInstance(t, new DummyProviderOptions())!));
    var health1 = manager.GetHealthService().Snapshot; // snapshot after optional failure
    Assert.Equal(HealthStatus.Degraded, health1.OverallStatus);
        Assert.Single(health1.Rules);
        Assert.Equal(1, health1.Rules[0].FailureCount);
        Assert.Equal(RuleResultStatus.Down, health1.Rules[0].Status);

        // Second attempt: call Initialize again (idempotent pattern not guaranteed) - we re-create manager to simulate retry
    var manager2 = ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(NullLogger.Instance).UseProviderFactory((t, _) => (ConfigurationProvider)Activator.CreateInstance(t, new DummyProviderOptions())!));

    var health2 = manager2.GetHealthService().Snapshot;
    Assert.Equal(HealthStatus.Healthy, health2.OverallStatus);
        Assert.Equal(RuleResultStatus.Up, health2.Rules[0].Status);
        Assert.Equal(0, health2.Rules[0].FailureCount); // Reset after success
    }

    public sealed class RecoveryConfig { public string Name { get; set; } = string.Empty; public int Value { get; set; } }
}



