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
    /// Original rules are completely skipped — ideal when providers would fail in test environment.
    /// Returns a <see cref="TestOverrideBuilder"/> that can be further composed (e.g. chaining
    /// <c>.ReplaceSecretsSetup(...)</c>) and acts as a disposable scope.
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <param name="setup">Optional function to build test setup using the fluent API.</param>
    /// <returns>An auto-activating builder / disposable scope.</returns>
    /// <example>
    /// <code>
    /// using var _ = CocoarTestConfiguration.ReplaceConfiguration(
    ///     rule => [
    ///         rule.For&lt;DbConfig&gt;().FromStatic(_ => new DbConfig { Connection = testDb })
    ///     ]);
    /// </code>
    /// </example>
    public static TestOverrideBuilder ReplaceConfiguration(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
        => new TestOverrideBuilder(autoActivate: true).ReplaceConfiguration(rules, setup);

    /// <summary>
    /// Appends test rules to the end of configured rules (last-write-wins).
    /// Original rules execute first, then test rules override specific values.
    /// Returns a <see cref="TestOverrideBuilder"/> that can be further composed and acts as a disposable scope.
    /// </summary>
    /// <param name="rules">Function to build test rules using the fluent API.</param>
    /// <param name="setup">Optional function to build test setup using the fluent API.</param>
    /// <returns>An auto-activating builder / disposable scope.</returns>
    /// <example>
    /// <code>
    /// using var _ = CocoarTestConfiguration.AppendConfiguration(
    ///     rule => [
    ///         rule.For&lt;DbConfig&gt;().FromStatic(_ => new DbConfig { Connection = testDb })
    ///     ]);
    /// </code>
    /// </example>
    public static TestOverrideBuilder AppendConfiguration(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
        => new TestOverrideBuilder(autoActivate: true).AppendConfiguration(rules, setup);

    /// <summary>
    /// Replaces the secrets setup used during ConfigManager initialization.
    /// Returns a <see cref="TestOverrideBuilder"/> that can be further composed and acts as a disposable scope.
    /// </summary>
    /// <param name="configure">Raw delegate that receives a SecretsBuilder (typed by Secrets package extension).</param>
    /// <returns>An auto-activating builder / disposable scope.</returns>
    public static TestOverrideBuilder ReplaceSecretsSetup(Delegate configure)
        => new TestOverrideBuilder(autoActivate: true).ReplaceSecretsSetupCore(configure);

    /// <summary>
    /// Applies an existing <see cref="TestConfigurationContext"/> to the current async context.
    /// Use this in test class constructors to bridge the AsyncLocal gap between fixtures and test methods.
    /// </summary>
    /// <param name="context">The test configuration context to apply.</param>
    /// <returns>A scope that clears the test configuration when disposed.</returns>
    /// <example>
    /// <code>
    /// public class MyTests : IClassFixture&lt;IntegrationTestFixture&gt;, IDisposable
    /// {
    ///     public MyTests(IntegrationTestFixture fixture)
    ///     {
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
    /// Sets the active context. Called by <see cref="TestOverrideBuilder"/> after each mutation.
    /// </summary>
    internal static void SetContext(TestConfigurationContext context) =>
        s_testContext.Value = context;
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
/// Use <see cref="TestConfigurationContext.Replace"/> or <see cref="TestConfigurationContext.Append"/>
/// as convenient factory methods, or build via <see cref="TestOverrideBuilder"/> for the fixture pattern.
/// </remarks>
public sealed class TestConfigurationContext
{
    /// <summary>
    /// Gets the rules builder function for this test configuration, or null if no rules override is set.
    /// </summary>
    public Func<RulesBuilder, ConfigRule[]>? Rules { get; }

    /// <summary>
    /// Gets the mode for this test configuration (Replace or Append), or null if no rules override is set.
    /// </summary>
    public TestConfigurationMode? ConfigurationMode { get; }

    /// <summary>
    /// Gets the optional setup builder function for this test configuration.
    /// </summary>
    public Func<SetupBuilder, SetupDefinition[]>? Setup { get; }

    /// <summary>
    /// Optional custom serializer options for this test configuration context.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Raw secrets setup override delegate. Typed as <see cref="Delegate"/> to avoid a hard dependency
    /// on the Secrets package from the core library. The Secrets package casts this to the correct type.
    /// </summary>
    internal Delegate? SecretsSetupOverride { get; init; }

    internal TestConfigurationContext(
        Func<RulesBuilder, ConfigRule[]>? rules = null,
        Func<SetupBuilder, SetupDefinition[]>? setup = null,
        TestConfigurationMode? configurationMode = null,
        Delegate? secretsSetupOverride = null)
    {
        Rules = rules;
        Setup = setup;
        ConfigurationMode = configurationMode;
        SecretsSetupOverride = secretsSetupOverride;
    }

    /// <summary>
    /// Creates a test configuration context that replaces all configured rules.
    /// Original rules are completely skipped — ideal when providers would fail in the test environment.
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
    ///             ]);
    /// }
    /// </code>
    /// </example>
    public static TestConfigurationContext Replace(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new(rules, setup, TestConfigurationMode.Replace);
    }

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
    ///             ]);
    /// }
    /// </code>
    /// </example>
    public static TestConfigurationContext Append(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new(rules, setup, TestConfigurationMode.Append);
    }
}

/// <summary>
/// Fluent builder for composing test configuration overrides.
/// Returned by <see cref="CocoarTestConfiguration.ReplaceConfiguration"/>,
/// <see cref="CocoarTestConfiguration.AppendConfiguration"/>, and
/// <see cref="CocoarTestConfiguration.ReplaceSecretsSetup"/>.
/// When constructed via those static methods (auto-activate mode) each chained call immediately
/// activates the accumulated context in the current async scope.
/// Use <c>new TestOverrideBuilder()</c> (no-arg) for the fixture pattern — context is only activated
/// when you later call <see cref="CocoarTestConfiguration.Apply"/>.
/// Disposing a builder created in auto-activate mode calls <see cref="CocoarTestConfiguration.Clear"/>.
/// </summary>
public sealed class TestOverrideBuilder : IDisposable
{
    private readonly bool _autoActivate;
    private Func<RulesBuilder, ConfigRule[]>? _rules;
    private Func<SetupBuilder, SetupDefinition[]>? _setup;
    private TestConfigurationMode? _configurationMode;
    private Delegate? _secretsSetupOverride;

    /// <summary>
    /// Creates a builder for the fixture pattern. Does NOT auto-activate.
    /// Call <see cref="Build"/> and then pass the result to <see cref="CocoarTestConfiguration.Apply"/>.
    /// </summary>
    public TestOverrideBuilder() { }

    internal TestOverrideBuilder(bool autoActivate) => _autoActivate = autoActivate;

    /// <summary>
    /// Sets the rules override to Replace mode (original rules are skipped).
    /// </summary>
    public TestOverrideBuilder ReplaceConfiguration(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _setup = setup ?? _setup;
        _configurationMode = TestConfigurationMode.Replace;
        if (_autoActivate) CocoarTestConfiguration.SetContext(Build());
        return this;
    }

    /// <summary>
    /// Sets the rules override to Append mode (test rules follow original rules, last-write-wins).
    /// </summary>
    public TestOverrideBuilder AppendConfiguration(
        Func<RulesBuilder, ConfigRule[]> rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _setup = setup ?? _setup;
        _configurationMode = TestConfigurationMode.Append;
        if (_autoActivate) CocoarTestConfiguration.SetContext(Build());
        return this;
    }

    /// <summary>
    /// Sets a raw secrets setup override delegate (used internally and by the Secrets package extension).
    /// </summary>
    internal TestOverrideBuilder ReplaceSecretsSetupCore(Delegate configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _secretsSetupOverride = configure;
        if (_autoActivate) CocoarTestConfiguration.SetContext(Build());
        return this;
    }

    /// <summary>
    /// Called by the Secrets package extension to store a strongly-typed override.
    /// </summary>
    public void SetSecretsSetupOverride(Delegate configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _secretsSetupOverride = configure;
        if (_autoActivate) CocoarTestConfiguration.SetContext(Build());
    }

    /// <summary>
    /// Builds a snapshot <see cref="TestConfigurationContext"/> without activating it.
    /// </summary>
    public TestConfigurationContext Build() =>
        new(_rules, _setup, _configurationMode, _secretsSetupOverride);

    /// <summary>
    /// Clears the active test configuration context. Only meaningful in auto-activate mode.
    /// </summary>
    public void Dispose() => CocoarTestConfiguration.Clear();
}

/// <summary>
/// A disposable scope that clears test configuration when disposed.
/// Use with <c>using</c> statements for exception-safe cleanup.
/// </summary>
/// <example>
/// <code>
/// using var _ = CocoarTestConfiguration.Apply(context);
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
