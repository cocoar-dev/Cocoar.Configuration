using System.Text.Json;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Testing;

/// <summary>
/// Provides test-friendly configuration overrides using AsyncLocal for isolated test contexts.
/// Use this in integration tests to override configuration rules without modifying application code.
/// </summary>
/// <remarks>
/// <para>
/// <b>AsyncLocal Context Behavior:</b> AsyncLocal flows automatically through async/await within the same
/// async context. However, xUnit creates separate async contexts for fixture setup vs test methods.
/// Configuration set in a fixture's <c>InitializeAsync()</c> will NOT be visible in test methods.
/// </para>
/// <para>
/// <b>Solution:</b> For fixture-based patterns, store the <see cref="TestConfigurationContext"/> in the fixture,
/// then call <see cref="Apply"/> in each test class constructor to bridge the context gap.
/// See <see cref="TestConfigurationContext.Replace"/> and <see cref="TestConfigurationContext.Append"/>
/// for convenient factory methods.
/// </para>
/// </remarks>
public static class CocoarTestConfiguration
{
    private static readonly AsyncLocal<TestConfigurationContext?> s_testContext = new();

    /// <summary>
    /// Replaces all configured rules with test-specific rules.
    /// Original rules are completely skipped - ideal when providers would fail in test environment.
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <param name="setup">Optional function to build test setup using the fluent API.</param>
    /// <returns>A scope that clears the test configuration when disposed.</returns>
    /// <example>
    /// <code>
    /// using var _ = CocoarTestConfiguration.ReplaceAllRules(
    ///     rule => [
    ///         rule.For&lt;DbConfig&gt;().FromStatic(_ => new DbConfig { Connection = testDb })
    ///     ],
    ///     setup => [
    ///         setup.Secrets().AllowPlaintext()
    ///     ]);
    /// // Test configuration automatically cleared when scope is disposed
    /// </code>
    /// </example>
    public static TestConfigurationScope ReplaceAllRules(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        s_testContext.Value = new TestConfigurationContext(rules, TestConfigurationMode.Replace, setup);
        return new TestConfigurationScope();
    }

    /// <summary>
    /// Appends test rules to the end of configured rules (last-write-wins).
    /// Original rules execute first, then test rules override specific values.
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <param name="setup">Optional function to build test setup using the fluent API.</param>
    /// <returns>A scope that clears the test configuration when disposed.</returns>
    /// <example>
    /// <code>
    /// using var _ = CocoarTestConfiguration.AppendTestRules(
    ///     rule => [
    ///         rule.For&lt;DbConfig&gt;().FromStatic(_ => new DbConfig { Connection = testDb })
    ///     ],
    ///     setup => [
    ///         setup.Secrets().AllowPlaintext()
    ///     ]);
    /// // Test configuration automatically cleared when scope is disposed
    /// </code>
    /// </example>
    public static TestConfigurationScope AppendTestRules(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        s_testContext.Value = new TestConfigurationContext(rules, TestConfigurationMode.Append, setup);
        return new TestConfigurationScope();
    }

    /// <summary>
    /// Applies setup overrides only, keeping original rules unchanged.
    /// Use this when you only need to override setup options like AllowPlaintext() without changing rules.
    /// </summary>
    /// <param name="setup">Function to build test setup using the fluent API.</param>
    /// <returns>A scope that clears the test configuration when disposed.</returns>
    /// <example>
    /// <code>
    /// using var _ = CocoarTestConfiguration.WithSetup(setup => [
    ///     setup.Secrets().AllowPlaintext()
    /// ]);
    /// // Original rules are preserved, but setup includes test overrides
    /// </code>
    /// </example>
    public static TestConfigurationScope WithSetup(Func<SetupBuilder, SetupDefinition[]> setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        // Create context with empty rules in Append mode (original rules preserved)
        s_testContext.Value = new TestConfigurationContext(
            rules => [],
            TestConfigurationMode.Append,
            setup);
        return new TestConfigurationScope();
    }

    /// <summary>
    /// Applies an existing <see cref="TestConfigurationContext"/> to the current async context.
    /// Use this in test class constructors to bridge the AsyncLocal gap between fixtures and test methods.
    /// </summary>
    /// <param name="context">The test configuration context to apply.</param>
    /// <returns>A scope that clears the test configuration when disposed.</returns>
    /// <example>
    /// <code>
    /// public class IntegrationTestFixture
    /// {
    ///     public TestConfigurationContext TestContext { get; } =
    ///         TestConfigurationContext.Replace(rule => [
    ///             rule.For&lt;DbConfig&gt;().FromStatic(_ => new DbConfig { Connection = "test-db" })
    ///         ]);
    /// }
    ///
    /// public class MyTests : IClassFixture&lt;IntegrationTestFixture&gt;, IDisposable
    /// {
    ///     public MyTests(IntegrationTestFixture fixture)
    ///     {
    ///         // Bridge the async context gap - one line!
    ///         CocoarTestConfiguration.Apply(fixture.TestContext);
    ///     }
    ///
    ///     public void Dispose() => CocoarTestConfiguration.Clear();
    /// }
    /// </code>
    /// </example>
    public static TestConfigurationScope Apply(TestConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        s_testContext.Value = context;
        return new TestConfigurationScope();
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

    /// <summary>
    /// Optional custom serializer options for test scenarios.
    /// Set by extension packages (e.g., Cocoar.Configuration.Secrets) to handle
    /// special types during FromStatic serialization.
    /// </summary>
    public static JsonSerializerOptions? TestSerializerOptions { get; set; }
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
/// <remarks>
/// Store instances of this class in test fixtures and use <see cref="CocoarTestConfiguration.Apply"/>
/// in test class constructors to bridge the async context gap between fixture setup and test methods.
/// </remarks>
public sealed class TestConfigurationContext
{
    /// <summary>
    /// Gets the rules builder function for this test configuration.
    /// </summary>
    public Func<RulesBuilder, ConfigRule[]> Rules { get; }

    /// <summary>
    /// Gets the mode for this test configuration (Replace or Append).
    /// </summary>
    public TestConfigurationMode Mode { get; }

    /// <summary>
    /// Gets the optional setup builder function for this test configuration.
    /// </summary>
    public Func<SetupBuilder, SetupDefinition[]>? Setup { get; }

    /// <summary>
    /// Creates a new test configuration context.
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <param name="mode">The configuration override mode.</param>
    /// <param name="setup">Optional function to build test setup using the fluent API.</param>
    public TestConfigurationContext(
        Func<RulesBuilder, ConfigRule[]> rules,
        TestConfigurationMode mode,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules = rules;
        Mode = mode;
        Setup = setup;
    }

    /// <summary>
    /// Creates a test configuration context that replaces all configured rules.
    /// Original rules are completely skipped - ideal when providers would fail in test environment.
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <param name="setup">Optional function to build test setup using the fluent API.</param>
    /// <returns>A new test configuration context in Replace mode.</returns>
    /// <example>
    /// <code>
    /// public class IntegrationTestFixture
    /// {
    ///     public TestConfigurationContext TestContext { get; } =
    ///         TestConfigurationContext.Replace(
    ///             rule => [
    ///                 rule.For&lt;DbConfig&gt;().FromStatic(_ => new DbConfig { Connection = "test-db" })
    ///             ],
    ///             setup => [
    ///                 setup.Secrets().AllowPlaintext()
    ///             ]);
    /// }
    /// </code>
    /// </example>
    public static TestConfigurationContext Replace(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
        => new(rules, TestConfigurationMode.Replace, setup);

    /// <summary>
    /// Creates a test configuration context that appends test rules to configured rules.
    /// Original rules execute first, then test rules override specific values (last-write-wins).
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <param name="setup">Optional function to build test setup using the fluent API.</param>
    /// <returns>A new test configuration context in Append mode.</returns>
    /// <example>
    /// <code>
    /// public class IntegrationTestFixture
    /// {
    ///     public TestConfigurationContext TestContext { get; } =
    ///         TestConfigurationContext.Append(
    ///             rule => [
    ///                 rule.For&lt;DbConfig&gt;().FromStatic(_ => new DbConfig { MaxConnections = 5 })
    ///             ],
    ///             setup => [
    ///                 setup.Secrets().AllowPlaintext()
    ///             ]);
    /// }
    /// </code>
    /// </example>
    public static TestConfigurationContext Append(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
        => new(rules, TestConfigurationMode.Append, setup);
}

/// <summary>
/// A disposable scope that clears test configuration when disposed.
/// Use with <c>using</c> statements for exception-safe cleanup.
/// </summary>
/// <example>
/// <code>
/// using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [...]);
/// // Test runs here
/// // Configuration automatically cleared when scope is disposed
/// </code>
/// </example>
public readonly struct TestConfigurationScope : IDisposable
{
    /// <summary>
    /// Gets whether test configuration is currently active.
    /// </summary>
#pragma warning disable CA1822 // Member does not access instance data - intentional instance property for API convenience
    public bool IsActive => CocoarTestConfiguration.IsActive;
#pragma warning restore CA1822

    /// <summary>
    /// Clears the test configuration context.
    /// </summary>
    public void Dispose() => CocoarTestConfiguration.Clear();
}
