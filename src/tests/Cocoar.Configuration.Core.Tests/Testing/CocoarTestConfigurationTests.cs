using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Testing;

namespace Cocoar.Configuration.Core.Tests.Testing;

public class CocoarTestConfigurationTests
{
    public CocoarTestConfigurationTests()
    {
        // Ensure clean state before each test
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void ReplaceConfiguration_SetsContextInReplaceMode()
    {
        // Act
        CocoarTestConfiguration.ReplaceConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.Equal(TestConfigurationMode.Replace, CocoarTestConfiguration.Current!.ConfigurationMode);

        // Cleanup
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void AppendConfiguration_SetsContextInAppendMode()
    {
        // Act
        CocoarTestConfiguration.AppendConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.Equal(TestConfigurationMode.Append, CocoarTestConfiguration.Current!.ConfigurationMode);

        // Cleanup
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void Clear_RemovesContext()
    {
        // Arrange
        CocoarTestConfiguration.ReplaceConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);
        Assert.True(CocoarTestConfiguration.IsActive);

        // Act
        CocoarTestConfiguration.Clear();

        // Assert
        Assert.False(CocoarTestConfiguration.IsActive);
        Assert.Null(CocoarTestConfiguration.Current);
    }

    [Fact]
    public void ReplaceConfiguration_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() => CocoarTestConfiguration.ReplaceConfiguration(null!));
    }

    [Fact]
    public void AppendConfiguration_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() => CocoarTestConfiguration.AppendConfiguration(null!));
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}

public class TestConfigurationScopeTests
{
    public TestConfigurationScopeTests()
    {
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void ReplaceConfiguration_ReturnsBuilder()
    {
        // Act
        var builder = CocoarTestConfiguration.ReplaceConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert — builder is active (auto-activated)
        Assert.True(CocoarTestConfiguration.IsActive);

        // Cleanup
        builder.Dispose();
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void AppendConfiguration_ReturnsBuilder()
    {
        // Act
        var builder = CocoarTestConfiguration.AppendConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);

        // Cleanup
        builder.Dispose();
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void Scope_ClearsConfigurationOnDispose()
    {
        // Arrange
        Assert.False(CocoarTestConfiguration.IsActive);

        // Act & Assert
        using (var scope = CocoarTestConfiguration.ReplaceConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]))
        {
            Assert.True(CocoarTestConfiguration.IsActive);
        }

        // Configuration is cleared after scope disposal
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void Scope_ClearsConfigurationEvenOnException()
    {
        // Arrange
        Assert.False(CocoarTestConfiguration.IsActive);

        // Act
        try
        {
            using var scope = CocoarTestConfiguration.ReplaceConfiguration(rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ]);

            Assert.True(CocoarTestConfiguration.IsActive);
            throw new InvalidOperationException("Simulated exception");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert - still cleared
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void UsingPattern_WorksAsExpected()
    {
        // Arrange & Act
        using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "scoped-test" })
        ]);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.Equal(TestConfigurationMode.Replace, CocoarTestConfiguration.Current!.ConfigurationMode);
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}

public class TestConfigurationContextTests
{
    [Fact]
    public void Replace_CreatesContextInReplaceMode()
    {
        // Act
        var context = TestConfigurationContext.Replace(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.Equal(TestConfigurationMode.Replace, context.ConfigurationMode);
        Assert.NotNull(context.Rules);
    }

    [Fact]
    public void Append_CreatesContextInAppendMode()
    {
        // Act
        var context = TestConfigurationContext.Append(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.Equal(TestConfigurationMode.Append, context.ConfigurationMode);
        Assert.NotNull(context.Rules);
    }

    [Fact]
    public void Replace_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() => TestConfigurationContext.Replace(null!));
    }

    [Fact]
    public void Append_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() => TestConfigurationContext.Append(null!));
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}

public class TestOverrideBuilderTests
{
    [Fact]
    public void Builder_ReplaceConfiguration_SetsReplaceMode()
    {
        // Arrange & Act
        var context = new TestOverrideBuilder()
            .ReplaceConfiguration(rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ])
            .Build();

        // Assert
        Assert.Equal(TestConfigurationMode.Replace, context.ConfigurationMode);
        Assert.NotNull(context.Rules);
    }

    [Fact]
    public void Builder_AppendConfiguration_SetsAppendMode()
    {
        // Arrange & Act
        var context = new TestOverrideBuilder()
            .AppendConfiguration(rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ])
            .Build();

        // Assert
        Assert.Equal(TestConfigurationMode.Append, context.ConfigurationMode);
        Assert.NotNull(context.Rules);
    }

    [Fact]
    public void Builder_ReplaceConfiguration_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestOverrideBuilder().ReplaceConfiguration(null!));
    }

    [Fact]
    public void Builder_AppendConfiguration_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestOverrideBuilder().AppendConfiguration(null!));
    }

    [Fact]
    public void Builder_Build_DoesNotActivate()
    {
        // Arrange
        CocoarTestConfiguration.Clear();

        // Act — fixture pattern: build without activating
        var context = new TestOverrideBuilder()
            .ReplaceConfiguration(rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ])
            .Build();

        // Assert — not yet active
        Assert.False(CocoarTestConfiguration.IsActive);
        Assert.NotNull(context);

        CocoarTestConfiguration.Clear();
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}

public class ApplyMethodTests
{
    public ApplyMethodTests()
    {
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void Apply_SetsContextFromExistingInstance()
    {
        // Arrange
        var context = TestConfigurationContext.Replace(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "applied" })
        ]);

        Assert.False(CocoarTestConfiguration.IsActive);

        // Act
        using var scope = CocoarTestConfiguration.Apply(context);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.Same(context, CocoarTestConfiguration.Current);
    }

    [Fact]
    public void Apply_ThrowsOnNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => CocoarTestConfiguration.Apply(null!));
    }

    [Fact]
    public void Apply_ReturnsScope()
    {
        // Arrange
        var context = TestConfigurationContext.Replace(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Act
        var scope = CocoarTestConfiguration.Apply(context);

        // Assert
        Assert.True(scope.IsActive);

        // Cleanup
        scope.Dispose();
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void Apply_ScopeDisposeClearsContext()
    {
        // Arrange
        var context = TestConfigurationContext.Replace(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Act
        using (var scope = CocoarTestConfiguration.Apply(context))
        {
            Assert.True(CocoarTestConfiguration.IsActive);
        }

        // Assert
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void Apply_CanBeCalledMultipleTimes_EachScopeClears()
    {
        // Arrange
        var context1 = TestConfigurationContext.Replace(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "first" })
        ]);
        var context2 = TestConfigurationContext.Replace(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "second" })
        ]);

        // Act & Assert
        using (var scope1 = CocoarTestConfiguration.Apply(context1))
        {
            Assert.Same(context1, CocoarTestConfiguration.Current);
        }
        Assert.False(CocoarTestConfiguration.IsActive);

        using (var scope2 = CocoarTestConfiguration.Apply(context2))
        {
            Assert.Same(context2, CocoarTestConfiguration.Current);
        }
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}

public class ConfigManagerIntegrationTests
{
    public ConfigManagerIntegrationTests()
    {
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void ConfigManager_AppliesTestOverrides_ReplaceMode()
    {
        // Arrange
        using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig
            {
                Connection = "test-connection",
                MaxConnections = 42
            })
        ]);

        // Act - ConfigManager with original rules
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig
            {
                Connection = "original-connection",
                MaxConnections = 10
            })
        ]));

        var config = configManager.GetRequiredConfig<TestConfig>();

        // Assert - Test overrides are used
        Assert.Equal("test-connection", config.Connection);
        Assert.Equal(42, config.MaxConnections);
    }

    [Fact]
    public void ConfigManager_AppliesTestOverrides_AppendMode()
    {
        // Arrange
        using var _ = CocoarTestConfiguration.AppendConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig
            {
                MaxConnections = 999
            })
        ]);

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig
            {
                Connection = "original-connection",
                MaxConnections = 10
            })
        ]));

        var config = configManager.GetRequiredConfig<TestConfig>();

        // Assert - Test override merged (last-write-wins)
        Assert.Equal(999, config.MaxConnections);
    }

    [Fact]
    public void ConfigManager_WorksNormally_WhenNoTestOverride()
    {
        // Arrange - No test configuration set

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig
            {
                Connection = "normal-connection",
                MaxConnections = 50
            })
        ]));

        var config = configManager.GetRequiredConfig<TestConfig>();

        // Assert - Normal behavior
        Assert.Equal("normal-connection", config.Connection);
        Assert.Equal(50, config.MaxConnections);
    }

    [Fact]
    public void ConfigManager_AppliesContextFromApply()
    {
        // Arrange
        var context = TestConfigurationContext.Replace(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig
            {
                Connection = "applied-connection",
                MaxConnections = 123
            })
        ]);

        using var _ = CocoarTestConfiguration.Apply(context);

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig
            {
                Connection = "original-connection",
                MaxConnections = 10
            })
        ]));

        var config = configManager.GetRequiredConfig<TestConfig>();

        // Assert
        Assert.Equal("applied-connection", config.Connection);
        Assert.Equal(123, config.MaxConnections);
    }

    private sealed class TestConfig
    {
        public string Connection { get; set; } = "";
        public int MaxConnections { get; set; }
    }
}

public class SetupOverrideTests
{
    public SetupOverrideTests()
    {
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void ReplaceConfiguration_WithSetup_SetsContextWithBothRulesAndSetup()
    {
        // Arrange
        Func<Fluent.RulesBuilder, Rules.ConfigRule[]> rules = rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ];
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        using var _ = CocoarTestConfiguration.ReplaceConfiguration(rules, setup);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.NotNull(CocoarTestConfiguration.Current!.Rules);
        Assert.NotNull(CocoarTestConfiguration.Current.Setup);
        Assert.Equal(TestConfigurationMode.Replace, CocoarTestConfiguration.Current.ConfigurationMode);

        // Cleanup
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void AppendConfiguration_WithSetup_SetsContextWithBothRulesAndSetup()
    {
        // Arrange
        Func<Fluent.RulesBuilder, Rules.ConfigRule[]> rules = rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ];
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        using var _ = CocoarTestConfiguration.AppendConfiguration(rules, setup);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.NotNull(CocoarTestConfiguration.Current!.Rules);
        Assert.NotNull(CocoarTestConfiguration.Current.Setup);
        Assert.Equal(TestConfigurationMode.Append, CocoarTestConfiguration.Current.ConfigurationMode);

        // Cleanup
        CocoarTestConfiguration.Clear();
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}

public class TestConfigurationContextSetupTests
{
    [Fact]
    public void Replace_WithSetup_CreatesContextWithSetup()
    {
        // Arrange
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        var context = TestConfigurationContext.Replace(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ],
            setup);

        // Assert
        Assert.Equal(TestConfigurationMode.Replace, context.ConfigurationMode);
        Assert.NotNull(context.Rules);
        Assert.Same(setup, context.Setup);
    }

    [Fact]
    public void Append_WithSetup_CreatesContextWithSetup()
    {
        // Arrange
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        var context = TestConfigurationContext.Append(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ],
            setup);

        // Assert
        Assert.Equal(TestConfigurationMode.Append, context.ConfigurationMode);
        Assert.NotNull(context.Rules);
        Assert.Same(setup, context.Setup);
    }

    [Fact]
    public void Builder_WithRulesAndSetup_BuiltContextHasBoth()
    {
        // Arrange
        Func<Fluent.RulesBuilder, Rules.ConfigRule[]> rules = rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ];
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        var context = new TestOverrideBuilder()
            .ReplaceConfiguration(rules, setup)
            .Build();

        // Assert
        Assert.Same(rules, context.Rules);
        Assert.Same(setup, context.Setup);
        Assert.Equal(TestConfigurationMode.Replace, context.ConfigurationMode);
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}

public class ConfigManagerSetupIntegrationTests
{
    public ConfigManagerSetupIntegrationTests()
    {
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void ConfigManager_AppliesTestSetupOverrides_WithReplaceConfiguration()
    {
        // Arrange
        var testSetupCalled = false;
        using var _ = CocoarTestConfiguration.ReplaceConfiguration(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ],
            setup =>
            {
                testSetupCalled = true;
                return [];
            });

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "original" })
            ],
            setup => []));

        // Assert
        Assert.True(testSetupCalled);
    }

    [Fact]
    public void ConfigManager_AppliesTestSetupOverrides_WithAppendConfiguration()
    {
        // Arrange
        var testSetupCalled = false;
        using var _ = CocoarTestConfiguration.AppendConfiguration(
            rule => [],
            setup =>
            {
                testSetupCalled = true;
                return [];
            });

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "original" })
            ],
            setup => []));

        // Assert
        Assert.True(testSetupCalled);
    }

    [Fact]
    public void ConfigManager_WorksWithoutSetup_WhenTestSetupIsNull()
    {
        // Arrange
        var configuredSetupCalled = false;
        using var _ = CocoarTestConfiguration.ReplaceConfiguration(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ]); // No setup parameter

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "original" })
            ],
            setup =>
            {
                configuredSetupCalled = true;
                return [];
            }));

        // Assert - Configured setup should still run
        Assert.True(configuredSetupCalled);
    }

    [Fact]
    public void ConfigManager_AppliesTestSetupOverrides_FromAppliedContext()
    {
        // Arrange
        var testSetupCalled = false;
        var context = TestConfigurationContext.Replace(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
            ],
            setup =>
            {
                testSetupCalled = true;
                return [];
            });

        using var _ = CocoarTestConfiguration.Apply(context);

        // Act
        var configManager = ConfigManager.Create(c => c.UseConfiguration(
            rule => [
                rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "original" })
            ],
            setup => []));

        // Assert
        Assert.True(testSetupCalled);
        var config = configManager.GetRequiredConfig<TestConfig>();
        Assert.Equal("test", config.Value);
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}
