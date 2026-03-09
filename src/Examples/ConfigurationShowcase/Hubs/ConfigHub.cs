using Microsoft.AspNetCore.SignalR;

namespace ConfigurationShowcase.Hubs;

/// <summary>
/// SignalR hub for pushing real-time configuration updates to external consumers.
/// Push-only: clients subscribe by connecting; no client-to-server methods needed.
/// </summary>
public sealed class ConfigHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", $"Connected at {DateTime.UtcNow:O}");
        await base.OnConnectedAsync();
    }
}
