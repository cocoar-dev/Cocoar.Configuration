using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Cocoar.Configuration.Providers;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Cocoar.Configuration.Yaml;

/// <summary>
/// Reads configuration from a YAML file. Converts YAML to the UTF-8 JSON the pipeline merges, mapping
/// <b>plain</b> scalars to their JSON types (YAML core schema: <c>true</c>/<c>false</c> → boolean,
/// integers/floats → number, <c>null</c>/<c>~</c> → null) so values bind like JSON. Quoted and block scalars
/// stay strings. Reactive: the file is watched and re-parsed on change (via <see cref="FileBackedProvider"/>).
/// </summary>
public sealed class YamlFileProvider(FileSourceProviderOptions options) : FileBackedProvider(options)
{
    protected override byte[] ParseToJsonBytes(byte[] rawFileBytes, string filename)
    {
        var yaml = Encoding.UTF8.GetString(rawFileBytes);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return "{}"u8.ToArray();
        }

        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0)
        {
            return "{}"u8.ToArray();
        }

        var node = ConvertNode(stream.Documents[0].RootNode);
        return node is null ? "{}"u8.ToArray() : Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    private static JsonNode? ConvertNode(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
                var obj = new JsonObject();
                foreach (var entry in map.Children)
                {
                    var key = (entry.Key as YamlScalarNode)?.Value ?? entry.Key.ToString();
                    obj[key ?? string.Empty] = ConvertNode(entry.Value);
                }

                return obj;

            case YamlSequenceNode seq:
                var arr = new JsonArray();
                foreach (var item in seq.Children)
                {
                    arr.Add(ConvertNode(item));
                }

                return arr;

            case YamlScalarNode scalar:
                return ConvertScalar(scalar);

            default:
                return null;
        }
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        // Only PLAIN scalars get YAML core-schema type inference; quoted ("…"/'…') and block (|/>) scalars
        // are always strings.
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(scalar.Value ?? string.Empty);
        }

        var v = scalar.Value;
        if (string.IsNullOrEmpty(v))
        {
            return null; // a plain empty scalar (`key:`) is null
        }

        if (v is "null" or "Null" or "NULL" or "~")
        {
            return null;
        }

        if (v is "true" or "True" or "TRUE")
        {
            return JsonValue.Create(true);
        }

        if (v is "false" or "False" or "FALSE")
        {
            return JsonValue.Create(false);
        }

        if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return JsonValue.Create(l);
        }

        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return JsonValue.Create(d);
        }

        return JsonValue.Create(v);
    }
}
