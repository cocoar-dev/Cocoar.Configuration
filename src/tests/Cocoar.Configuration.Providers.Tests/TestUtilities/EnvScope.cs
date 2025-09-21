using System;

namespace Cocoar.Configuration.Providers.Tests;

/// <summary>
/// Utility to isolate environment variable mutations within a using scope.
/// Restores previous value (or unsets) on dispose.
/// Deterministic: only touches Process-level environment.
/// </summary>
public sealed class EnvScope : IDisposable
{
    private readonly string _name;
    private readonly string? _original;
    private readonly bool _existed;

    private EnvScope(string name, string? newValue, bool set)
    {
        _name = name;
    _original = System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        _existed = _original != null;
        if (set)
            System.Environment.SetEnvironmentVariable(name, newValue, EnvironmentVariableTarget.Process);
        else
            System.Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
    }

    public static EnvScope Set(string name, string value) => new(name, value, set: true);
    public static EnvScope Unset(string name) => new(name, null, set: false);

    public void Dispose()
    {
        if (_existed)
            System.Environment.SetEnvironmentVariable(_name, _original, EnvironmentVariableTarget.Process);
        else
            System.Environment.SetEnvironmentVariable(_name, null, EnvironmentVariableTarget.Process);
    }
}
