using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;

namespace Cocoar.Configuration.Tests;

public class ObservableInterfaceBindingTests
{
    public interface IAppSettings
    {
        string Name { get; }
        int Value { get; }
        bool Enabled { get; }
    }

    public class AppSettings : IAppSettings
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public bool Enabled { get; set; }
    }

    [Fact]
    public void GetReactiveConfig_WorksWithInterfaceBinding()
    {
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, "{ \"Name\": \"InterfaceTest\", \"Value\": 123, \"Enabled\": true }");

        try
        {
            var rules = new[]
            {
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath))
                    .For<AppSettings>().Required().Build(),
            };

            var bindings = new BindingSpec[]
            {
                Bind.Type<AppSettings>().To<IAppSettings>()
            };

            var manager = new ConfigManager(rules, bindings, NullLogger.Instance).Initialize();
            
            // Test that we can get a reactive config for the interface type
            var reactiveConfig = manager.GetReactiveConfig<IAppSettings>();
            
            // Test current value access
            var currentValue = reactiveConfig.CurrentValue;
            Assert.NotNull(currentValue);
            Assert.Equal("InterfaceTest", currentValue.Name);
            Assert.Equal(123, currentValue.Value);
            Assert.True(currentValue.Enabled);
            
            // Test observable functionality  
            IAppSettings? emittedValue = null;
            var subscription = reactiveConfig.Subscribe(config =>
            {
                emittedValue = config;
            });

            // Should have immediate emission with proper interface binding
            Assert.NotNull(emittedValue);
            Assert.Equal("InterfaceTest", emittedValue.Name);
            Assert.Equal(123, emittedValue.Value);
            Assert.True(emittedValue.Enabled);

            subscription.Dispose();
            manager.Dispose();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void GetReactiveConfig_WithFileUpdates_WorksWithInterfaceBinding()
    {
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, "{ \"Name\": \"ReactiveInterface\", \"Value\": 456, \"Enabled\": false }");

        try
        {
            var rules = new[]
            {
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath))
                    .For<AppSettings>().Required().Build(),
            };

            var bindings = new BindingSpec[]
            {
                Bind.Type<AppSettings>().To<IAppSettings>()
            };

            var manager = new ConfigManager(rules, bindings, NullLogger.Instance).Initialize();
            
            // Test that we can get a reactive config for the interface type
            var reactiveConfig = manager.GetReactiveConfig<IAppSettings>();
            
            // Test immediate current value access
            var currentValue = reactiveConfig.CurrentValue;
            Assert.NotNull(currentValue);
            Assert.Equal("ReactiveInterface", currentValue.Name);
            Assert.Equal(456, currentValue.Value);
            Assert.False(currentValue.Enabled);
            
            // Test that it's also observable
            var receivedValues = new List<IAppSettings>();
            var subscription = reactiveConfig.Subscribe(config =>
            {
                if (config != null) receivedValues.Add(config);
            });

            // Should have immediate emission from subscription
            Assert.Single(receivedValues);
            Assert.Equal("ReactiveInterface", receivedValues[0].Name);

            subscription.Dispose();
            manager.Dispose();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task InterfaceAndConcreteObservables_ShareSameUpdates()
    {
        var tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, "{ \"Name\": \"Initial\", \"Value\": 1, \"Enabled\": true }");

        try
        {
            var rules = new[]
            {
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(tempPath, TimeSpan.FromMilliseconds(50)))
                    .For<AppSettings>().Required().Build(),
            };

            var bindings = new BindingSpec[]
            {
                Bind.Type<AppSettings>().To<IAppSettings>()
            };

            var manager = new ConfigManager(rules, bindings, NullLogger.Instance, debounceMilliseconds: 100).Initialize();
            
            var concreteReactive = manager.GetReactiveConfig<AppSettings>();
            var interfaceReactive = manager.GetReactiveConfig<IAppSettings>();
            
            var concreteValues = new List<AppSettings>();
            var interfaceValues = new List<IAppSettings>();
            
            var subscription1 = concreteReactive.Subscribe(config =>
            {
                if (config != null) concreteValues.Add(config);
            });
            
            var subscription2 = interfaceReactive.Subscribe(config =>
            {
                if (config != null) interfaceValues.Add(config);
            });

            // Should have initial emissions
            Assert.Single(concreteValues);
            Assert.Single(interfaceValues);
            Assert.Equal("Initial", concreteValues[0].Name);
            Assert.Equal("Initial", interfaceValues[0].Name);

            // Wait a bit to make sure initial load is complete
            await Task.Delay(200);

            // Modify the configuration file
            File.WriteAllText(tempPath, "{ \"Name\": \"Updated\", \"Value\": 999, \"Enabled\": false }");

            // Wait for change propagation
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(8) && 
                   (concreteValues.Count < 2 || interfaceValues.Count < 2))
            {
                await Task.Delay(100);
            }

            // Both should have received updates
            Assert.True(concreteValues.Count >= 2, $"Expected at least 2 concrete values, got {concreteValues.Count}");
            Assert.True(interfaceValues.Count >= 2, $"Expected at least 2 interface values, got {interfaceValues.Count}");
            
            var latestConcrete = concreteValues.Last();
            var latestInterface = interfaceValues.Last();
            
            Assert.Equal("Updated", latestConcrete.Name);
            Assert.Equal("Updated", latestInterface.Name);
            Assert.Equal(999, latestConcrete.Value);
            Assert.Equal(999, latestInterface.Value);

            subscription1.Dispose();
            subscription2.Dispose();
            manager.Dispose();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}