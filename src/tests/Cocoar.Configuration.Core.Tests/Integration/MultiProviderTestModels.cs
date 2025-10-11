using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Core.Tests.TestUtilities;

namespace Cocoar.Configuration.Core.Tests.Integration;

public static class MultiProviderTestModels
{
    public class AppConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Version { get; set; }
        public DatabaseConfig Database { get; set; } = new();
        public FeatureFlags Features { get; set; } = new();
    }

    public class DatabaseConfig
    {
        public string ConnectionString { get; set; } = string.Empty;
        public int Timeout { get; set; }
        public bool EnableRetry { get; set; }
    }

    public class FeatureFlags
    {
        public bool EnableNewUI { get; set; }
        public bool EnableLogging { get; set; }
        public string LogLevel { get; set; } = "Info";
    }

    public class ComplexConfig
    {
        public string AppName { get; set; } = string.Empty;
        public DatabaseSection Database { get; set; } = new();
        public FeaturesSection Features { get; set; } = new();
        public LegacySettingsSection? LegacySettings { get; set; }
    }

    public class DatabaseSection
    {
        public string Server { get; set; } = string.Empty;
        public int Port { get; set; }
    }

    public class FeaturesSection
    {
        public bool Alpha { get; set; }
        public bool Beta { get; set; }
    }

    public class LegacySettingsSection
    {
        public string Mode { get; set; } = string.Empty;
        public int Timeout { get; set; }
    }

    public class NestedConfig
    {
        public RootSection Root { get; set; } = new();
    }

    public class RootSection
    {
        public StaticSection Static { get; set; } = new();
        public DynamicSection? Dynamic { get; set; }
    }

    public class StaticSection
    {
        public string Value { get; set; } = string.Empty;
    }

    public class DynamicSection
    {
        public string Content { get; set; } = string.Empty;
    }

    public class SelectMountConfig
    {
        public string DefaultValue { get; set; } = string.Empty;
        public MountedSection? MountedSection { get; set; }
    }

    public class MountedSection
    {
        public string Data { get; set; } = string.Empty;
    }

    public class SimpleConfig
    {
        public string Value { get; set; } = string.Empty;
    }

    public class MergeTestConfig
    {
        public string? A { get; set; }
        public string? B { get; set; }
        public string? C { get; set; }
    }

    public class AppConfigWithArray
    {
        public string Name { get; set; } = string.Empty;
        public string[] Environments { get; set; } = Array.Empty<string>();
    }

    public class ScalarMergeConfig
    {
        public string Database { get; set; } = string.Empty;
    }

    public class ArrayMergeConfig
    {
        public ArrayMergeSettings Settings { get; set; } = new();
    }

    public class ArrayMergeSettings
    {
        public string Primary { get; set; } = string.Empty;
        public string Secondary { get; set; } = string.Empty;
    }

    public class NullMergeConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool Enabled { get; set; }
        public double Score { get; set; }
    }

    public class SnapshotConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public static bool JsonElementsEqual(JsonElement element1, JsonElement element2)
    {
        if (element1.ValueKind != element2.ValueKind)
        {
            return false;
        }

        return element1.ValueKind switch
        {
            JsonValueKind.Object => CompareObjects(element1, element2),
            JsonValueKind.Array => CompareArrays(element1, element2),
            JsonValueKind.String => element1.GetString() == element2.GetString(),
            JsonValueKind.Number => element1.GetRawText() == element2.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => element1.GetBoolean() == element2.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false
        };
    }

    private static bool CompareObjects(JsonElement obj1, JsonElement obj2)
    {
        var props1 = obj1.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        var props2 = obj2.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        if (props1.Count != props2.Count)
        {
            return false;
        }

        foreach (var kvp in props1)
        {
            if (!props2.TryGetValue(kvp.Key, out var value2) || !JsonElementsEqual(kvp.Value, value2))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CompareArrays(JsonElement arr1, JsonElement arr2)
    {
        var items1 = arr1.EnumerateArray().ToArray();
        var items2 = arr2.EnumerateArray().ToArray();

        if (items1.Length != items2.Length)
        {
            return false;
        }

        for (var i = 0; i < items1.Length; i++)
        {
            if (!JsonElementsEqual(items1[i], items2[i]))
            {
                return false;
            }
        }

        return true;
    }
}
