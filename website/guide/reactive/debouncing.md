# Debouncing

When a configuration source changes, the engine doesn't recompute immediately. It waits for a quiet period to coalesce rapid changes into a single recompute.

## Why Debounce?

File saves often trigger multiple file system events in quick succession. An editor saving a file might emit Created, Changed, Changed within milliseconds. Without debouncing, each event would trigger a full recompute — wasting resources and flooding subscribers with intermediate states.

## Default Behavior

The default debounce interval is **300 milliseconds**:

1. A change is detected (file modified, HTTP poll returned new data, etc.)
2. The engine starts a 300ms timer
3. If more changes arrive during those 300ms, the timer resets
4. After 300ms of quiet, the recompute runs

This is **trailing-edge debounce** — the recompute fires after the storm of changes passes.

## Configuring the Interval

Set the debounce interval when creating the ConfigManager:

```csharp
var manager = ConfigManager.Create(c => c
    .UseDebounce(500)  // 500ms debounce
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json"),
    ]));
```

```csharp
// ASP.NET Core
builder.AddCocoarConfiguration(c => c
    .UseDebounce(500)
    .UseConfiguration(rule => [ /* ... */ ]));
```

| Value | Effect |
|---|---|
| `0` | No debounce — recompute fires immediately on every change |
| `300` (default) | Good balance for most applications |
| `1000+` | Use when sources are noisy or recomputes are expensive |

## What Gets Coalesced

Debouncing operates across **all providers**. If a file change and an HTTP poll arrive within the debounce window, they're coalesced into one recompute.

The engine tracks which rule triggered the change and recomputes from the **earliest changed rule** forward. This ensures all downstream dependencies are updated correctly.

## Changes During Recompute

If a change arrives while a recompute is already running:

1. The change is noted (tracked as "during-run")
2. The current recompute finishes
3. A new recompute is scheduled with a trailing debounce
4. The trailing recompute picks up the changes that arrived during the previous run

This prevents lost updates without causing recompute storms.
