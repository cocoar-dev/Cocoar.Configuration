# MicrosoftConfigurationSourceProvider

Use any Microsoft.Extensions.Configuration IConfigurationSource as a Cocoar provider rule.

- Options: `MicrosoftConfigurationSourceProviderOptions(IConfigurationSource source)`
- Query: `MicrosoftConfigurationSourceProviderQueryOptions(configurationPrefix?)`
- Change semantics: relies on the underlying source/provider; many built-in configuration sources don't push change notifications here, so treat it as snapshot unless your source supports reloads.

## When to use

- You already have configuration sources/components built on IConfiguration and want to compose them with Cocoar rules.
- Bridging between app-specific IConfiguration pipelines and Cocoar’s rules/merge model.

## Example

```csharp
using Cocoar.Configuration.Fluent;
using Microsoft.Extensions.Configuration;
using Cocoar.Configuration.MicrosoftAdapter;

var rules = new[]
{
    Rules.FromProvider<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions, MicrosoftConfigurationSourceProviderQueryOptions>(
        instanceOptions: _ => new MicrosoftConfigurationSourceProviderOptions(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string,string?>
                {
                    ["My:Section:Enabled"] = "true",
                    ["My:Section:Value"] = "42",
                })
                .Sources[0]
        ),
        queryOptions: _ => new MicrosoftConfigurationSourceProviderQueryOptions(configurationPrefix: "My:Section")
    )
    .ForType<MySettings>()
    .Optional()
    .Build()
};
```

## Notes

- Keys are flattened with ':' separators; later rules override earlier ones (last-wins).
- Arrays are not merged—only objects.
- See the root `README.md` ("How it works") and `src/Cocoar.Configuration/README.md` for full merge semantics, recompute behavior, and dynamic dependencies.

Known gaps
- Change notifications depend on the underlying source; many sources act like snapshots. Consider combining with other providers if you need change-driven recompute.
