# ADR-001: Using Cocoar.Capabilities for Cross-Assembly Extensibility

**Status:** Accepted  
**Date:** 2024-09-14  
**Decision Makers:** Core Team

---

## Context

Cocoar.Configuration has a **fundamental architectural requirement**: extension methods from separate assemblies (like `Cocoar.Configuration.DI`) need to attach metadata to builder objects from the core assembly without creating circular dependencies.

### The Problem

Consider this user-facing API:

```csharp
builder.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("config.json")
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>(),  // Core assembly
    setup.ConcreteType<AppSettings>().AsSingleton()              // DI assembly
]);
```

**Requirements:**
1. Core assembly defines `ConcreteTypeSetup<T>` with `.ExposeAs<T>()` method
2. DI assembly adds `.AsSingleton()` extension method to the same builder type
3. Both methods must attach metadata to the **same builder instance**
4. Core assembly **cannot reference** DI assembly (would create circular dependency)
5. DI assembly needs to retrieve **all metadata** from all builders at registration time
6. The fluent API must remain clean and chainable
7. Third parties should be able to add their own extensions

### Why Standard .NET Patterns Don't Work

| Pattern | Why It Fails |
|---------|--------------|
| **Dictionary&lt;object, object>** | No type safety, can't compose multiple metadata types, external global state |
| **Reflection Attributes** | Compile-time only, can't configure same type differently in different contexts |
| **Method Parameters** | Destroys fluent API, parameter explosion, not extensible from other assemblies |
| **Builder Internal State** | Core must know about ALL extension metadata types → circular dependencies |

After several days exploring alternatives, we concluded: **There is no standard .NET pattern that solves this problem without compromising on quality.**

---

## Decision

We will use **[Cocoar.Capabilities](https://github.com/cocoar-dev/Cocoar.Capabilities)**, a separate open-source library that enables type-safe metadata attachment across assembly boundaries.

### Why This Library?

1. **Solves the exact problem** - Designed specifically for cross-assembly metadata composition
2. **Zero coupling** - Core and DI assemblies remain completely independent
3. **Type-safe** - All metadata is strongly typed at compile time
4. **Proven pattern** - Used successfully in production
5. **Reusable** - Separate library means it can be used in other projects
6. **Third-party extensible** - Anyone can add capabilities without modifying our code

### How We Use It

**Core Assembly** attaches primary and core metadata:
```csharp
public sealed class ConcreteTypeSetup<T> : SetupDefinition
{
    internal ConcreteTypeSetup(ConfigManagerCapabilityScope capabilityScope) 
        : base(capabilityScope)
    {
        capabilityScope.Compose(this).WithPrimary(
            new ConcreteTypePrimary<SetupDefinition>(typeof(T)));
    }
    
    public ConcreteTypeSetup<T> ExposeAs<TInterface>()
    {
        GetComposer(this).Add(
            new ExposeAsCapability<SetupDefinition>(typeof(TInterface)));
        return this;
    }
}
```

**DI Assembly** extends without coupling:
```csharp
public static class ConcreteTypeSetupExtensions
{
    public static ConcreteTypeSetup<T> AsSingleton<T>(this ConcreteTypeSetup<T> builder)
    {
        SetupDefinition.GetComposer(builder).Add(
            new ServiceLifetimeCapability<SetupDefinition>(ServiceLifetime.Singleton, null));
        return builder;
    }
}
```

**Registration Time** retrieves all metadata:
```csharp
foreach (var builder in configManager.SetupDefinitions)
{
    if (!scope.Compositions.TryGet(builder, out var composition))
        continue;
    
    // Get metadata from ANY assembly that added capabilities
    var typeCapability = composition.TryGetPrimaryAs<ConcreteTypePrimary<SetupDefinition>>();
    var lifetimes = composition.GetAll<ServiceLifetimeCapability<SetupDefinition>>();
    var exposures = composition.GetAll<ExposeAsCapability<SetupDefinition>>();
    
    // Use all metadata for registration
}
```

---

## Consequences

### Positive
✅ **Zero Coupling** - Core and DI assemblies are completely independent  
✅ **Type Safety** - All capabilities are strongly typed at compile time  
✅ **Fluent API Preserved** - Method chaining works seamlessly  
✅ **Extensible** - Third parties can add their own capabilities  
✅ **Testable** - Capabilities can be mocked and verified independently  

### Trade-offs
⚠️ **Additional Dependency** - Requires Cocoar.Capabilities package  
⚠️ **Learning Curve** - Contributors must understand the Capabilities pattern  
⚠️ **Debugging Indirection** - Capability composition harder to trace than direct fields  

**Mitigation:** This ADR and inline documentation explain the pattern. The complexity is hidden from end users - they just use the fluent API.

---

## Alternatives Considered

### Alternative 1: Accept Circular Dependencies
Make Core reference DI, DI reference Core.

**Rejected:** Violates clean architecture, makes testing difficult, prevents third-party extensions.

### Alternative 2: ConditionalWeakTable
Use .NET's ConditionalWeakTable for metadata storage.

**Rejected:** Still lacks type safety and composition. Cannot distinguish primary vs secondary metadata.

### Alternative 3: Event-Based Registration
Use events to notify about builder creation and metadata.

**Rejected:** Ordering issues, no clear ownership, difficult to query later, thread safety nightmares.

### Alternative 4: Custom Metadata Interface
Create `IMetadataCarrier` interface for builders.

**Rejected:** Requires all builders to implement interface (intrusive), string-keyed dictionaries lose type safety, doesn't solve cross-assembly attachment.

---

## Implementation Details

### Key Classes
- `ConfigManagerCapabilityScope` - Manages capability lifetime for this ConfigManager instance
- `SetupDefinition` - Abstract base that provides access to `CapabilityScope` and `Composer`
- `ConcreteTypeSetup<T>` / `InterfaceTypeSetup<T>` - Builders that compose capabilities
- `ExposureRegistry` - Reads capabilities at registration time

### Capability Records
- `ConcreteTypePrimary<T>` - Primary: The type being configured
- `ExposeAsCapability<T>` - Secondary: Interface exposure for DI
- `DeserializeToCapability<T>` - Secondary: Interface→Concrete mapping for deserialization
- `ServiceLifetimeCapability<T>` - Secondary (DI): Service lifetime metadata

### Thread Safety
Cocoar.Capabilities handles thread safety internally using concurrent collections. Composers are immutable after Build().

---

## References

### External
- **Cocoar.Capabilities Repository:** https://github.com/cocoar-dev/Cocoar.Capabilities
- **Blog Post (Context):** [The Cross-Assembly Metadata Problem in .NET](https://dev.to/bwi/the-cross-assembly-metadata-problem-in-net-and-how-i-solved-it-14eo)

### Internal
- `Core/ConfigManagerCapabilityScope.cs` - Scope implementation
- `Configure/SetupBuilder.cs` - Builder API using Capabilities
- `Infrastructure/ExposureRegistry.cs` - Capability retrieval example
- `DI/ConcreteTypeSetupExtensions.cs` - Cross-assembly extension example

---

## FAQ

**Q: Why not just use a dictionary and accept the type-safety loss?**  
A: Type safety isn't just about compile-time errors - it's about maintainability. When adding a new capability type, the compiler helps find all places that need updates. With untyped dictionaries, we lose that safety net.

**Q: Doesn't this feel like over-engineering?**  
A: The problem only appears simple because the requirements are subtle. We explored simpler solutions for several days before choosing Capabilities - none worked without compromising on architecture or extensibility.

**Q: What if Cocoar.Capabilities changes or breaks compatibility?**  
A: Both libraries are maintained by the same author/team. Breaking changes would be coordinated across both projects. The separation provides architectural benefits (reusability, clear boundaries) without introducing external dependency risk.

**Q: Why create a separate library instead of keeping it internal?**  
A: (1) The pattern is reusable across other projects. (2) Clear separation of concerns - Capabilities is a general-purpose library. (3) Forces a clean API boundary. (4) Can be used by third-party extensions to this project.

---

**Status:** ✅ Accepted and Implemented  
**Next Review:** When significant new extension patterns emerge or if third-party extension requirements change

---

**Revision History:**

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-11-12 | 1.0 | Initial ADR | Core Team |

