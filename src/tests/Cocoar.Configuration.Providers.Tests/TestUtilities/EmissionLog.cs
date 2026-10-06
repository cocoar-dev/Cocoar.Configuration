using System.Text.Json;

namespace Cocoar.Configuration.Providers.Tests.TestUtilities;

/// <summary>
/// Records provider emissions. Emissions arrive on a file-watcher thread while the test thread reads
/// them, so a plain list would be read while it is being resized.
/// </summary>
public sealed class EmissionLog
{
    private readonly List<JsonElement> _items = [];

    public void Add(JsonElement emission)
    {
        lock (_items) _items.Add(emission);
    }

    public int Count
    {
        get { lock (_items) return _items.Count; }
    }

    public JsonElement Last
    {
        get { lock (_items) return _items[^1]; }
    }

    public JsonElement[] Snapshot()
    {
        lock (_items) return [.. _items];
    }
}
