using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Testing;

/// <summary>
/// Provides test-friendly configuration overrides using AsyncLocal for isolated test contexts.
/// Use this in integration tests to override configuration rules without modifying application code.
/// </summary>
public static class CocoarTestConfiguration
{
    private static readonly AsyncLocal<TestConfigurationContext?> s_testContext = new();

    /// <summary>
    /// Replaces all configured rules with test-specific rules.
    /// Original rules are completely skipped - ideal when providers would fail in test environment.
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <example>
    /// <code>
    /// CocoarTestConfiguration.ReplaceAllRules(rule => [
    ///     rule.For&lt;DbConfig&gt;().FromStatic(() => new DbConfig { Connection = testDb })
    /// ]);
    /// </code>
    /// </example>
    public static void ReplaceAllRules(Func<RulesBuilder, ConfigRule[]> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        s_testContext.Value = new TestConfigurationContext(rules, TestConfigurationMode.Replace);
    }

    /// <summary>
    /// Appends test rules to the end of configured rules (last-write-wins).
    /// Original rules execute first, then test rules override specific values.
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <example>
    /// <code>
    /// CocoarTestConfiguration.AppendTestRules(rule => [
    ///     rule.For&lt;DbConfig&gt;().FromStatic(() => new DbConfig { Connection = testDb })
    /// ]);
    /// </code>
    /// </example>
    public static void AppendTestRules(Func<RulesBuilder, ConfigRule[]> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        s_testContext.Value = new TestConfigurationContext(rules, TestConfigurationMode.Append);
    }

    /// <summary>
    /// Clears the test configuration context, restoring normal configuration behavior.
    /// </summary>
    public static void Clear()
    {
        s_testContext.Value = null;
    }

    /// <summary>
    /// Gets the current test configuration context, if any.
    /// </summary>
    public static TestConfigurationContext? Current => s_testContext.Value;

    /// <summary>
    /// Checks if test configuration is active in the current async context.
    /// </summary>
    public static bool IsActive => s_testContext.Value != null;
}

/// <summary>
/// Represents the mode for test configuration override.
/// </summary>
public enum TestConfigurationMode
{
    /// <summary>
    /// Replace all configured rules with test rules (original rules are skipped).
    /// </summary>
    Replace,

    /// <summary>
    /// Append test rules to the end of configured rules (last-write-wins merging).
    /// </summary>
    Append
}

/// <summary>
/// Holds the test configuration context stored in AsyncLocal.
/// </summary>
public sealed class TestConfigurationContext
{
    public Func<RulesBuilder, ConfigRule[]> Rules { get; }
    public TestConfigurationMode Mode { get; }

    public TestConfigurationContext(Func<RulesBuilder, ConfigRule[]> rules, TestConfigurationMode mode)
    {
        Rules = rules;
        Mode = mode;
    }
}
