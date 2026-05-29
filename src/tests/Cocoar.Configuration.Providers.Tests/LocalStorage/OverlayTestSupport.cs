using System.Text.Json.Serialization;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Providers.Tests.LocalStorage;

public sealed class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public NestedSettings Nested { get; set; } = new();
}

public sealed class NestedSettings
{
    public string? Level { get; set; }
    public int Count { get; set; }
}

public sealed class AttributedSettings
{
    [JsonPropertyName("custom_name")]
    public string? Renamed { get; set; }
}

public sealed class SecretSettings
{
    public Secret<string>? ApiKey { get; set; }
    public string? Plain { get; set; }
}

public sealed class IndexableSettings
{
    public List<string> Items { get; set; } = new();
}

/// <summary>In-memory storage backend for deterministic overlay tests (no file I/O).</summary>
public sealed class InMemoryBackend : IStorageBackend
{
    private byte[]? _data;

    public InMemoryBackend(byte[]? seed = null) => _data = seed;

    public byte[]? Current => _data;

    public Task<byte[]?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(_data);

    public Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        _data = data;
        return Task.CompletedTask;
    }
}
