// Migrated from root Examples/StaticProviderExample.cs

using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.StaticJsonProvider;

public sealed class CoreDefaults
{
    public string Feature { get; set; } = "A";
    public bool Enabled { get; set; } = true;
}

public sealed class Wrapper
{
    public CoreDefaults? Inner { get; set; }
}

public static class Program
{
    public static void Main(string[] args)
    {
        var rules = new []
        {
            Rule.From.Static(_ => new CoreDefaults { Feature = "A", Enabled = true })
                .For<CoreDefaults>()
                .Required()
                .Build(),
            Rule.From.Static(cm => new Wrapper { Inner = cm.GetRequiredConfig<CoreDefaults>() })
                .For<Wrapper>()
                .Required()
                .Build()
        };
        var manager = new ConfigManager(rules).Initialize();
        var wrapper = manager.GetRequiredConfig<Wrapper>();
        Console.WriteLine($"Wrapper has feature: {wrapper.Inner?.Feature}");
    }
}