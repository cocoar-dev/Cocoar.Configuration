---
description: Runnable example projects in src/Examples — file layering, conditional rules, providers (command-line, HTTP, custom), tuple reactive, secrets, ASP.NET Core, testing overrides
---

# Examples

Runnable example projects demonstrating individual features. Each is a standalone .NET project in [`src/Examples/`](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples).

## Configuration

| Example | What It Shows |
|---------|--------------|
| [BasicUsage](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/BasicUsage) | ASP.NET Core with file + environment layering |
| [FileLayering](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/FileLayering) | Multi-file layering (base / environment / local) |
| [ConditionalRulesExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/ConditionalRulesExample) | Config-aware conditional rules with `.When()` |
| [DynamicDependencies](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/DynamicDependencies) | Rules derived from earlier config |
| [ExposeExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/ExposeExample) | Interface exposure with `.ExposeAs<T>()` |
| [SimplifiedCoreExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/SimplifiedCoreExample) | Console app without DI using `ConfigManager.Create()` |

## Providers

| Example | What It Shows |
|---------|--------------|
| [CommandLineExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/CommandLineExample) | Command-line argument parsing with prefix filtering |
| [StaticProviderExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/StaticProviderExample) | Static and observable providers |
| [HttpPollingExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/HttpPollingExample) | Remote HTTP config (polling, SSE, one-time fetch) |
| [MicrosoftAdapterExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/MicrosoftAdapterExample) | Bridge existing `IConfiguration` sources |
| [GenericProviderAPI](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/GenericProviderAPI) | Building a custom provider |

## Reactive & ASP.NET Core

| Example | What It Shows |
|---------|--------------|
| [TupleReactiveExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/TupleReactiveExample) | Atomic multi-config snapshots with `IReactiveConfig<(T1, T2)>` |
| [AspNetCoreExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/AspNetCoreExample) | Full ASP.NET Core integration with health checks |

## Secrets

| Example | What It Shows |
|---------|--------------|
| [SecretsBasicExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/SecretsBasicExample) | `Secret<T>` with plaintext and lease pattern |
| [SecretsCertificateExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/SecretsCertificateExample) | Pre-encrypted secrets with X.509 certificates |

## Testing

| Example | What It Shows |
|---------|--------------|
| [TestingOverridesExample](https://github.com/cocoar-dev/Cocoar.Configuration/tree/develop/src/Examples/TestingOverridesExample) | `CocoarTestConfiguration` with Replace/Append modes |
