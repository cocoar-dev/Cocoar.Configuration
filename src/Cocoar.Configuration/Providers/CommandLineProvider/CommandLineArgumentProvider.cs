using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class CommandLineArgumentProvider(CommandLineProviderOptions options)
    : ConfigurationProvider<CommandLineProviderOptions, CommandLineProviderQueryOptions>(options)
{
    public override Task<byte[]> FetchConfigurationBytesAsync(CommandLineProviderQueryOptions queryOptions, CancellationToken ct = default)
    {
        var args = queryOptions.Args ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
        var switchPrefixes = queryOptions.SwitchPrefixes ?? ["--"];
        // Sort by length descending to match longest prefixes first (e.g., "--" before "-")
        var sortedPrefixes = switchPrefixes.OrderByDescending(p => p.Length).ToArray();
        var prefix = queryOptions.Prefix;
        var parsedArgs = ParseBasicPosixArguments(args, sortedPrefixes);

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in parsedArgs)
        {
            var key = kvp.Key;
            if (!string.IsNullOrEmpty(prefix))
            {
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                key = key[prefix.Length..];
            }
            
            AddToNestedDict(dict, key, kvp.Value);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(dict);
        return Task.FromResult(bytes);
    }

    private static Dictionary<string, string?> ParseBasicPosixArguments(string[] args, string[] switchPrefixes)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            
            string? matchedPrefix = null;
            foreach (var prefix in switchPrefixes)
            {
                if (arg.StartsWith(prefix))
                {
                    matchedPrefix = prefix;
                    break;
                }
            }
            
            if (matchedPrefix == null)
            {
                continue;
            }

            var key = arg[matchedPrefix.Length..];
            
            var equalsIndex = key.IndexOf('=');
            if (equalsIndex > 0)
            {
                var keyPart = key[..equalsIndex];
                var valuePart = key[(equalsIndex + 1)..];
                result[keyPart] = valuePart;
            }
            else if (i + 1 < args.Length && !StartsWithAnyPrefix(args[i + 1], switchPrefixes))
            {
                result[key] = args[i + 1];
                i++;
            }
            else
            {
                result[key] = "true";
            }
        }

        return result;
    }

    private static bool StartsWithAnyPrefix(string arg, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (arg.StartsWith(prefix))
            {
                return true;
            }
        }
        return false;
    }

    private static void AddToNestedDict(IDictionary<string, object?> dict, string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var parts = key.Split([":", "__"], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var current = dict;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var seg = parts[i];
            if (!current.TryGetValue(seg, out var next) || next is not IDictionary<string, object?> nextDict)
            {
                nextDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[seg] = nextDict;
            }

            current = nextDict;
        }

        current[parts[^1]] = value;
    }

    public static Rules.ConfigRule CreateRule<T>(string[]? args = null, string[]? switchPrefixes = null, string? prefix = null, bool required = false)
    {
        return Rules.ConfigRule.Create<CommandLineArgumentProvider, CommandLineProviderOptions, CommandLineProviderQueryOptions>(
            _ => new(),
            _ => new(args, switchPrefixes, prefix),
            typeof(T),
            new(Required: required, UseWhen: null)
        );
    }

    public override IObservable<byte[]> ChangesAsBytes(CommandLineProviderQueryOptions queryOptions)
        => System.Reactive.Linq.Observable.Never<byte[]>();
}
