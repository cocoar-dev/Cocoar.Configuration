using System.Text;
using System.Text.Json;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Reads configuration from an <c>.ini</c> file: <c>[section]</c> headers, <c>key=value</c> lines, and
/// whole-line comments (<c>;</c> or <c>#</c>). Section and key names nest with <c>.</c> or <c>:</c> (e.g.
/// <c>[Db]</c> + <c>Port=5432</c> → <c>{ "Db": { "Port": "5432" } }</c>; <c>[Db.Primary]</c> nests further).
/// Values are emitted as JSON strings; the binder coerces them to the target type. Surrounding quotes are
/// stripped. Inline comments are <b>not</b> stripped, so values containing <c>;</c> or <c>#</c> (e.g. a
/// connection string) survive intact. Reactive: the file is watched and re-parsed on change (via
/// <see cref="FileBackedProvider"/>).
/// </summary>
public sealed class IniProvider(FileSourceProviderOptions options) : FileBackedProvider(options)
{
    protected override byte[] ParseToJsonBytes(byte[] rawFileBytes, string filename)
    {
        var text = Encoding.UTF8.GetString(rawFileBytes);
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var section = Array.Empty<string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
            {
                continue; // blank or whole-line comment
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                var name = line[1..^1].Trim();
                section = name.Length == 0 ? Array.Empty<string>() : SplitPath(name);
                continue;
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

            var path = section.Length == 0 ? SplitPath(key) : [.. section, .. SplitPath(key)];
            if (path.Length == 0)
            {
                continue;
            }

            AddNested(root, path, Unquote(line[(eq + 1)..].Trim()));
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static string[] SplitPath(string name) =>
        name.Split([':', '.'], StringSplitOptions.RemoveEmptyEntries);

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            var quote = value[0];
            if ((quote == '"' || quote == '\'') && value[^1] == quote)
            {
                return value[1..^1];
            }
        }

        // No inline-comment stripping: an unquoted value keeps any ';' or '#' (e.g. a connection string).
        return value;
    }

    private static void AddNested(Dictionary<string, object?> root, string[] path, string value)
    {
        var current = root;
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (!current.TryGetValue(path[i], out var next) || next is not Dictionary<string, object?> dict)
            {
                dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[path[i]] = dict;
            }

            current = dict;
        }

        current[path[^1]] = value;
    }
}
