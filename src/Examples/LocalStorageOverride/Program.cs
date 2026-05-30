using System.Diagnostics;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.LocalStorage;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.LocalStorageOverride;

// Demonstrates LocalStorage as a SPARSE OVERRIDE OVERLAY:
//   - lower layers (here, a static JSON layer) supply the DEFAULTS
//   - the application overrides INDIVIDUAL values at runtime via ILocalStorage<T>
//   - only the overridden keys are persisted; everything else keeps inheriting
//   - reset removes an override so the value falls back to the default again
public static class Program
{
    public sealed class SmtpSettings
    {
        public string? Host { get; set; }
        public int Port { get; set; }
        public bool UseSsl { get; set; }
    }

    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c.UseConfiguration(rules =>
        [
            // Defaults supplied by the normal sources (a file/env/etc. — a static layer here):
            rules.For<SmtpSettings>().FromStaticJson("""{ "Host": "smtp.default.com", "Port": 25, "UseSsl": false }"""),
            // The app-controlled override layer, placed AFTER so it wins for the keys it sets:
            rules.For<SmtpSettings>().FromLocalStorage(),
        ]));

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<ConfigManager>();
        var storage = provider.GetRequiredService<ILocalStorage<SmtpSettings>>();

        // Start from a clean overlay so the demo is deterministic across runs.
        await storage.ClearAsync();
        await WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Port == 25);
        Print("Defaults (overlay empty)", manager.GetConfig<SmtpSettings>()!);

        // Override a single value — only "Port" is persisted; Host/UseSsl keep inheriting.
        await storage.SetAsync(x => x.Port, 587);
        await WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Port == 587);
        Print("After SetAsync(x => x.Port, 587)", manager.GetConfig<SmtpSettings>()!);

        await storage.SetAsync(x => x.UseSsl, true);
        await WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.UseSsl);
        Print("After SetAsync(x => x.UseSsl, true)", manager.GetConfig<SmtpSettings>()!);

        // The raw stored overlay is sparse — only the two keys we set.
        Console.WriteLine($"\nPersisted overlay (sparse): {await storage.Overlay.ReadOverlayAsync()}");

        // Provenance for a management UI: base vs. effective vs. overridden, per key.
        Console.WriteLine("\nDescribeAsync() — per-key provenance:");
        foreach (var entry in await storage.DescribeAsync())
        {
            Console.WriteLine(
                $"  {entry.KeyPath,-8} base={Render(entry.BaseValue),-20} " +
                $"effective={Render(entry.EffectiveValue),-20} overridden={entry.IsOverridden}");
        }

        // Reset one override — the value falls back to the default.
        await storage.ResetAsync(x => x.Port);
        await WaitUntilAsync(() => manager.GetConfig<SmtpSettings>()!.Port == 25);
        Print("\nAfter ResetAsync(x => x.Port)", manager.GetConfig<SmtpSettings>()!);

        // Clear everything — back to pure defaults.
        await storage.ClearAsync();
        await WaitUntilAsync(() => !manager.GetConfig<SmtpSettings>()!.UseSsl);
        Print("After ClearAsync()", manager.GetConfig<SmtpSettings>()!);
    }

    private static void Print(string label, SmtpSettings s) =>
        Console.WriteLine($"{label,-40} Host={s.Host}, Port={s.Port}, UseSsl={s.UseSsl}");

    private static string Render(System.Text.Json.JsonElement? value) =>
        value is { } v ? v.GetRawText() : "(absent)";

    // Writes trigger a debounced recompute; poll briefly until the effective value reflects it.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            if (condition()) return;
            await Task.Delay(25);
        }
    }
}
