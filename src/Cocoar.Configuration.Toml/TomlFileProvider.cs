using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Cocoar.Configuration.Providers;
using Tomlyn.Model;

namespace Cocoar.Configuration.Toml;

/// <summary>
/// Reads configuration from a TOML file. Converts TOML to the UTF-8 JSON the pipeline merges. TOML is
/// strongly typed, so the mapping is unambiguous: strings, integers, floats and booleans map to their JSON
/// equivalents; tables map to objects; arrays (and arrays-of-tables) map to JSON arrays; date/time values
/// map to ISO-8601 strings. Reactive: the file is watched and re-parsed on change (via <see cref="FileBackedProvider"/>).
/// </summary>
public sealed class TomlFileProvider(FileSourceProviderOptions options) : FileBackedProvider(options)
{
    protected override byte[] ParseToJsonBytes(byte[] rawFileBytes, string filename)
    {
        var toml = Encoding.UTF8.GetString(rawFileBytes);
        if (string.IsNullOrWhiteSpace(toml))
        {
            return "{}"u8.ToArray();
        }

        // Deserialize into the dynamic TomlTable model. Throws TomlException on invalid TOML — the
        // FileBackedProvider contract treats a throw on the fetch path as a hard failure (Required rollback /
        // Optional degrade) and degrades to {} on the change path.
        // global:: qualifier: this namespace ends in ".Toml", which would otherwise shadow the Tomlyn types.
        var model = global::Tomlyn.TomlSerializer.Deserialize<TomlTable>(toml);
        return Encoding.UTF8.GetBytes(ConvertTable(model).ToJsonString());
    }

    private static JsonObject ConvertTable(TomlTable table)
    {
        var obj = new JsonObject();
        foreach (var entry in table)
        {
            obj[entry.Key] = ConvertValue(entry.Value);
        }

        return obj;
    }

    private static JsonNode? ConvertValue(object? value) => value switch
    {
        null => null,
        TomlTable t => ConvertTable(t),
        TomlTableArray ta => ConvertTableArray(ta),
        TomlArray a => ConvertArray(a),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        long l => JsonValue.Create(l),
        int i => JsonValue.Create((long)i),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create((double)f),
        // TOML date/time types — emit ISO-8601 strings; the binder coerces to DateTime/DateTimeOffset/etc.
        DateTime dt => JsonValue.Create(dt.ToString("o", CultureInfo.InvariantCulture)),
        DateTimeOffset dto => JsonValue.Create(dto.ToString("o", CultureInfo.InvariantCulture)),
        DateOnly d => JsonValue.Create(d.ToString("o", CultureInfo.InvariantCulture)),
        TimeOnly t => JsonValue.Create(t.ToString("o", CultureInfo.InvariantCulture)),
        _ => JsonValue.Create(value.ToString())
    };

    private static JsonArray ConvertArray(TomlArray array)
    {
        var arr = new JsonArray();
        foreach (var item in array)
        {
            arr.Add(ConvertValue(item));
        }

        return arr;
    }

    private static JsonArray ConvertTableArray(TomlTableArray tableArray)
    {
        var arr = new JsonArray();
        foreach (var table in tableArray)
        {
            arr.Add(ConvertTable(table));
        }

        return arr;
    }
}
