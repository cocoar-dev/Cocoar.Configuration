# Advanced Features

## Service Lifetimes & Keyed Services

Control how configuration types are registered in DI. Default is Singleton. You can specify Scoped/Transient and keyed services.

👉 Example: [`src/Examples/ServiceLifetimes/Program.cs`](../src/Examples/ServiceLifetimes/Program.cs)

---

## Generic Provider API

Use `Rule.From.Provider<TProvider, TOptions, TQuery>()` for full control over provider composition.

👉 Example: [`src/Examples/GenericProviderAPI/Program.cs`](../src/Examples/GenericProviderAPI/Program.cs)

---

## Microsoft Configuration Adapter

Plug any Microsoft `IConfigurationSource` (JSON, XML, Key Vault, User Secrets, etc.).

👉 Example: [`src/Examples/MicrosoftAdapterExample/Program.cs`](../src/Examples/MicrosoftAdapterExample/Program.cs)

---

## HTTP Polling Provider

Fetch config from HTTP endpoints with polling & change detection.

👉 Example: [`src/Examples/HttpPollingExample/Program.cs`](../src/Examples/HttpPollingExample/Program.cs)
