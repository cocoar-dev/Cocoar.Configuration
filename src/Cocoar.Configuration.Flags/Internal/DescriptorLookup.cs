using System.Reflection;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Bridges <see cref="FeatureFlagsSetupBuilder"/> and <see cref="EntitlementsSetupBuilder"/>
/// to the source-generated <c>CocoarFlagsDescriptors</c> class in the caller's assembly.
/// Called once per <c>Register&lt;T&gt;()</c> invocation at startup — not on hot paths.
/// </summary>
internal static class DescriptorLookup
{
    internal static FeatureFlagClassDescriptor? GetFlagsDescriptor(Type flagType)
    {
        var generated = flagType.Assembly
            .GetType("Cocoar.Configuration.Flags.Generated.CocoarFlagsDescriptors");
        var field = generated?.GetField("Flags",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return field?.GetValue(null)
            is IReadOnlyDictionary<Type, FeatureFlagClassDescriptor> dict
            && dict.TryGetValue(flagType, out var d) ? d : null;
    }

    internal static EntitlementClassDescriptor? GetEntitlementsDescriptor(Type entitlementType)
    {
        var generated = entitlementType.Assembly
            .GetType("Cocoar.Configuration.Flags.Generated.CocoarFlagsDescriptors");
        var field = generated?.GetField("Entitlements",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return field?.GetValue(null)
            is IReadOnlyDictionary<Type, EntitlementClassDescriptor> dict
            && dict.TryGetValue(entitlementType, out var d) ? d : null;
    }
}
