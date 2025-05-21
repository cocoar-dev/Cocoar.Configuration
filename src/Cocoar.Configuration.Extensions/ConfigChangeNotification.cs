using System.Text.Json;

namespace Cocoar.Configuration.Extensions;

public record ConfigChangeNotification(string? Part, JsonElement? NewValue, JsonElement? OldValue);