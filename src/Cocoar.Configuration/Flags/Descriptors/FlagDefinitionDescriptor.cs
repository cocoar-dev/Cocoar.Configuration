namespace Cocoar.Configuration.Flags;

/// <summary>
/// Compile-time descriptor for an individual flag defined within a <see cref="FeatureFlags"/> subclass.
/// </summary>
public sealed record FlagDefinitionDescriptor(
    string Name,
    string? Description);
