using System.Text;
using System.Text.Json;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Reads configuration from a <c>.env</c> file (12-factor style): <c>KEY=value</c> lines, <c>#</c> comments,
/// an optional <c>export</c> prefix, and quoted values. Keys nest with <c>:</c> or <c>__</c> (e.g.
/// <c>Db__Port=5432</c> → <c>{ "Db": { "Port": "5432" } }</c>), matching the environment-variable convention.
/// Values are emitted as JSON strings; the binder coerces them to the target type. Reactive: the file is watched
/// and re-parsed on change (via <see cref="FileBackedProvider"/>).
/// </summary>
public sealed class DotEnvProvider(FileSourceProviderOptions options) : FileBackedProvider(options)
{
    protected override byte[] ParseToJsonBytes(byte[] rawFileBytes, string filename)
    {
        var text = Encoding.UTF8.GetString(rawFileBytes);
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue; // no key, or no '='
            }

            var key = line[..eq].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            var value = Unquote(line[(eq + 1)..].Trim());
            AddNested(root, key, value);
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            var quote = value[0];
            if ((quote == '"' || quote == '\'') && value[^1] == quote)
            {
                var inner = value[1..^1];
                // Double-quoted values support common escapes; single-quoted are literal.
                return quote == '"'
                    ? inner.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\")
                    : inner;
            }
        }

        // Unquoted: strip a trailing inline comment (whitespace + '#').
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment >= 0 ? value[..comment].TrimEnd() : value;
    }

    private static void AddNested(Dictionary<string, object?> root, string key, string value)
    {
        var segments = key.Split([":", "__"], StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!current.TryGetValue(segments[i], out var next) || next is not Dictionary<string, object?> dict)
            {
                dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[segments[i]] = dict;
            }

            current = dict;
        }

        current[segments[^1]] = value;
    }
}
