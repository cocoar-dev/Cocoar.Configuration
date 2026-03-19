using System.Diagnostics;
using System.Reflection;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Bridges <see cref="FlagsBuilder"/> and <see cref="EntitlementsBuilder"/>
/// to the source-generated <c>CocoarFlagsDescriptors</c> class in the caller's assembly.
/// Called once per <c>Register&lt;T&gt;()</c> invocation at startup — not on hot paths.
/// </summary>
internal static class DescriptorLookup
{
    private const string GeneratedTypeName = "Cocoar.Configuration.Flags.Generated.CocoarFlagsDescriptors";

    internal static FeatureFlagClassDescriptor? GetFlagsDescriptor(Type flagType)
    {
        var generated = flagType.Assembly.GetType(GeneratedTypeName);
        if (generated is null)
        {
            WarnMissingGenerator(flagType);
            return null;
        }

        var field = generated.GetField("Flags",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return field?.GetValue(null)
            is IReadOnlyDictionary<Type, FeatureFlagClassDescriptor> dict
            && dict.TryGetValue(flagType, out var d) ? d : null;
    }

    internal static EntitlementClassDescriptor? GetEntitlementsDescriptor(Type entitlementType)
    {
        var generated = entitlementType.Assembly.GetType(GeneratedTypeName);
        if (generated is null)
        {
            WarnMissingGenerator(entitlementType);
            return null;
        }

        var field = generated.GetField("Entitlements",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return field?.GetValue(null)
            is IReadOnlyDictionary<Type, EntitlementClassDescriptor> dict
            && dict.TryGetValue(entitlementType, out var d) ? d : null;
    }

    private static void WarnMissingGenerator(Type type)
    {
        Trace.TraceWarning(
            $"Cocoar.Configuration: Source-generated '{GeneratedTypeName}' not found in assembly '{type.Assembly.GetName().Name}'. " +
            $"Ensure 'Cocoar.Configuration.Analyzers' is referenced with OutputItemType=\"Analyzer\". " +
            $"Without it, ExpiresAt defaults to MaxValue and flag/entitlement descriptions are unavailable.");
    }
}
