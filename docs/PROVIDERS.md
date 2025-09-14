# Providers Overview

## Built-in Providers

* **Static Provider** – in-memory seeding & dependent composition
* **File Provider** – JSON files with filesystem watching
* **Environment Provider** – environment variables with prefix filtering

## Extension Providers

* **HTTP Polling Provider** – remote polling with change detection
* **Microsoft Adapter** – integrate any `IConfigurationSource`

---

## Provider Features

| Provider          | Package   | Change Signal          | Notes                           |
| ----------------- | --------- | ---------------------- | ------------------------------- |
| Static            | Core      | ❌                      | Snapshot only                   |
| File (JSON)       | Core      | ✅ Debounced FS watcher | Good base layer                 |
| Environment       | Core      | ❌ Snapshot only        | Prefix filter                   |
| HTTP Polling      | Extension | ✅ Payload diff         | Optional headers, interval      |
| Microsoft Adapter | Extension | Depends                | Wrap any `IConfigurationSource` |

---

## Extensibility

Create custom providers by implementing the generic provider base and adding fluent entry points (e.g. `Rule.From.MyProvider()`).

See [Provider Development Guide](PROVIDER_DEV.md).
