using System.Text.Json;

namespace Cocoar.Configuration;

public record ConfigChangeNotification(string? Part, JsonElement? NewValue, JsonElement? OldValue);
