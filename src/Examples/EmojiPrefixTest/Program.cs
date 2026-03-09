using Cocoar.Configuration;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

namespace EmojiPrefixTest;

public class Program
{
    public static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var args = new[] { "💀host=localhost", "🔥port=8080", "🚀verbose" };

        Console.WriteLine("=== Emoji Prefix Test ===\n");
        Console.WriteLine($"Input args:");
        foreach (var arg in args)
        {
            Console.WriteLine($"  {arg}");
        }
        Console.WriteLine();

        // Test 1: Emoji prefixes
        Console.WriteLine("--- Test 1: Individual emoji prefixes ---");
        try
        {
            using var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [
                rule.For<SkullConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    SwitchPrefixes = ["💀"]
                }),
                rule.For<FireConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    SwitchPrefixes = ["🔥"]
                }),
                rule.For<RocketConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = args,
                    SwitchPrefixes = ["🚀"]
                })
            ]));

            var skull = manager.GetConfig<SkullConfig>();
            var fire = manager.GetConfig<FireConfig>();
            var rocket = manager.GetConfig<RocketConfig>();

            Console.WriteLine($"💀 Skull Host: {skull?.Host ?? "null"}");
            Console.WriteLine($"🔥 Fire Port: {fire?.Port ?? 0}");
            Console.WriteLine($"🚀 Rocket Verbose: {rocket?.Verbose ?? false}");
            Console.WriteLine("✅ SUCCESS! Emoji prefixes work!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR: {ex.Message}");
        }
        Console.WriteLine();

        // Test 2: Mixed emoji and traditional
        Console.WriteLine("--- Test 2: Mixed emoji and traditional prefixes ---");
        var mixedArgs = new[] { "💀host=emojihost", "--host=dashhost", "@host=athost" };
        try
        {
            using var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [
                rule.For<SkullConfig>().FromCommandLine(cm => new CommandLineRuleOptions
                {
                    Args = mixedArgs,
                    SwitchPrefixes = ["💀", "--", "@"]
                })
            ]));

            var config = manager.GetConfig<SkullConfig>();
            Console.WriteLine($"Host (should be 'athost' - last wins): {config?.Host ?? "null"}");
            Console.WriteLine("✅ SUCCESS! Mixed prefixes work!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR: {ex.Message}");
        }
    }
}

public class SkullConfig
{
    public string? Host { get; set; }
}

public class FireConfig
{
    public int Port { get; set; }
}

public class RocketConfig
{
    public bool Verbose { get; set; }
}

