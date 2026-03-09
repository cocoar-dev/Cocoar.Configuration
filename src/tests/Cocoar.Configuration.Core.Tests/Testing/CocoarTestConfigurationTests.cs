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
    public void ReplaceAllRules_SetsContextInReplaceMode()
    {
        // Act
        CocoarTestConfiguration.ReplaceAllRules(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.Equal(TestConfigurationMode.Replace, CocoarTestConfiguration.Current!.Mode);

        // Cleanup
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void AppendTestRules_SetsContextInAppendMode()
    {
        // Act
        CocoarTestConfiguration.AppendTestRules(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.Equal(TestConfigurationMode.Append, CocoarTestConfiguration.Current!.Mode);

        // Cleanup
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void Clear_RemovesContext()
    {
        // Arrange
        CocoarTestConfiguration.ReplaceAllRules(rule => [
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
    public void ReplaceAllRules_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() => CocoarTestConfiguration.ReplaceAllRules(null!));
    }

    [Fact]
    public void AppendTestRules_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() => CocoarTestConfiguration.AppendTestRules(null!));
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
    public void ReplaceAllRules_ReturnsScope()
    {
        // Act
        var scope = CocoarTestConfiguration.ReplaceAllRules(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.True(scope.IsActive);

        // Cleanup
        scope.Dispose();
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void AppendTestRules_ReturnsScope()
    {
        // Act
        var scope = CocoarTestConfiguration.AppendTestRules(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.True(scope.IsActive);

        // Cleanup
        scope.Dispose();
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void Scope_ClearsConfigurationOnDispose()
    {
        // Arrange
        Assert.False(CocoarTestConfiguration.IsActive);

        // Act & Assert
        using (var scope = CocoarTestConfiguration.ReplaceAllRules(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]))
        {
            Assert.True(scope.IsActive);
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
            using var scope = CocoarTestConfiguration.ReplaceAllRules(rule => [
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
        using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "scoped-test" })
        ]);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.Equal(TestConfigurationMode.Replace, CocoarTestConfiguration.Current!.Mode);
    }

    private sealed class TestConfig
    {
        public string Value { get; set; } = "";
    }
}

public class TestConfigurationContextTests
{
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        Func<Fluent.RulesBuilder, Rules.ConfigRule[]> rules = rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ];

        // Act
        var context = new TestConfigurationContext(rules, TestConfigurationMode.Replace);

        // Assert
        Assert.Same(rules, context.Rules);
        Assert.Equal(TestConfigurationMode.Replace, context.Mode);
    }

    [Fact]
    public void Constructor_ThrowsOnNullRules()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestConfigurationContext(null!, TestConfigurationMode.Replace));
    }

    [Fact]
    public void Replace_CreatesContextInReplaceMode()
    {
        // Act
        var context = TestConfigurationContext.Replace(rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ]);

        // Assert
        Assert.Equal(TestConfigurationMode.Replace, context.Mode);
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
        Assert.Equal(TestConfigurationMode.Append, context.Mode);
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
        using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [
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
        using var _ = CocoarTestConfiguration.AppendTestRules(rule => [
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
    public void WithSetup_SetsContextWithSetup()
    {
        // Arrange
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        using var _ = CocoarTestConfiguration.WithSetup(setup);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.NotNull(CocoarTestConfiguration.Current!.Setup);
        Assert.Equal(TestConfigurationMode.Append, CocoarTestConfiguration.Current.Mode);

        // Cleanup
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void WithSetup_ThrowsOnNullSetup()
    {
        Assert.Throws<ArgumentNullException>(() => CocoarTestConfiguration.WithSetup(null!));
    }

    [Fact]
    public void WithSetup_ReturnsScope()
    {
        // Act
        var scope = CocoarTestConfiguration.WithSetup(setup => []);

        // Assert
        Assert.True(scope.IsActive);

        // Cleanup
        scope.Dispose();
        Assert.False(CocoarTestConfiguration.IsActive);
    }

    [Fact]
    public void ReplaceAllRules_WithSetup_SetsContextWithBothRulesAndSetup()
    {
        // Arrange
        Func<Fluent.RulesBuilder, Rules.ConfigRule[]> rules = rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ];
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        using var _ = CocoarTestConfiguration.ReplaceAllRules(rules, setup);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.NotNull(CocoarTestConfiguration.Current!.Rules);
        Assert.NotNull(CocoarTestConfiguration.Current.Setup);
        Assert.Equal(TestConfigurationMode.Replace, CocoarTestConfiguration.Current.Mode);

        // Cleanup
        CocoarTestConfiguration.Clear();
    }

    [Fact]
    public void AppendTestRules_WithSetup_SetsContextWithBothRulesAndSetup()
    {
        // Arrange
        Func<Fluent.RulesBuilder, Rules.ConfigRule[]> rules = rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ];
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        using var _ = CocoarTestConfiguration.AppendTestRules(rules, setup);

        // Assert
        Assert.True(CocoarTestConfiguration.IsActive);
        Assert.NotNull(CocoarTestConfiguration.Current);
        Assert.NotNull(CocoarTestConfiguration.Current!.Rules);
        Assert.NotNull(CocoarTestConfiguration.Current.Setup);
        Assert.Equal(TestConfigurationMode.Append, CocoarTestConfiguration.Current.Mode);

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
    public void Constructor_WithSetup_SetsAllProperties()
    {
        // Arrange
        Func<Fluent.RulesBuilder, Rules.ConfigRule[]> rules = rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ];
        Func<SetupBuilder, SetupDefinition[]> setup = builder => [];

        // Act
        var context = new TestConfigurationContext(rules, TestConfigurationMode.Replace, setup);

        // Assert
        Assert.Same(rules, context.Rules);
        Assert.Same(setup, context.Setup);
        Assert.Equal(TestConfigurationMode.Replace, context.Mode);
    }

    [Fact]
    public void Constructor_WithoutSetup_SetsSetupToNull()
    {
        // Arrange
        Func<Fluent.RulesBuilder, Rules.ConfigRule[]> rules = rule => [
            rule.For<TestConfig>().FromStatic(_ => new TestConfig { Value = "test" })
        ];

        // Act
        var context = new TestConfigurationContext(rules, TestConfigurationMode.Replace);

        // Assert
        Assert.Same(rules, context.Rules);
        Assert.Null(context.Setup);
        Assert.Equal(TestConfigurationMode.Replace, context.Mode);
    }

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
        Assert.Equal(TestConfigurationMode.Replace, context.Mode);
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
        Assert.Equal(TestConfigurationMode.Append, context.Mode);
        Assert.NotNull(context.Rules);
        Assert.Same(setup, context.Setup);
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
    public void ConfigManager_AppliesTestSetupOverrides_WithReplaceAllRules()
    {
        // Arrange
        var testSetupCalled = false;
        using var _ = CocoarTestConfiguration.ReplaceAllRules(
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
    public void ConfigManager_AppliesTestSetupOverrides_WithAppendTestRules()
    {
        // Arrange
        var testSetupCalled = false;
        using var _ = CocoarTestConfiguration.AppendTestRules(
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
    public void ConfigManager_AppliesTestSetupOverrides_WithSetupOnly()
    {
        // Arrange
        var testSetupCalled = false;
        using var _ = CocoarTestConfiguration.WithSetup(setup =>
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

        // Assert - Original rules should still work
        var config = configManager.GetRequiredConfig<TestConfig>();
        Assert.Equal("original", config.Value);
        Assert.True(testSetupCalled);
    }

    [Fact]
    public void ConfigManager_MergesConfiguredAndTestSetup()
    {
        // Arrange
        var configuredSetupCalled = false;
        var testSetupCalled = false;

        using var _ = CocoarTestConfiguration.WithSetup(setup =>
        {
            testSetupCalled = true;
            return [];
        });

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

        // Assert - Both setups should be called (merged)
        Assert.True(configuredSetupCalled);
        Assert.True(testSetupCalled);
    }

    [Fact]
    public void ConfigManager_WorksWithoutSetup_WhenTestSetupIsNull()
    {
        // Arrange
        var configuredSetupCalled = false;
        using var _ = CocoarTestConfiguration.ReplaceAllRules(
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
