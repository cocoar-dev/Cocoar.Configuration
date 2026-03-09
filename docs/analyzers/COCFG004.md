# COCFG004 — Configuration Accessor Type Safety Violation

## What it detects

A call to `GetConfig<T>()` or `TryGetConfig<T>()` uses a type `T` that is a value type
(struct), which is not supported. Configuration types must be reference types (classes).

## Why it matters

The configuration system stores and retrieves instances by type. Value types cannot be
stored polymorphically and do not participate in the reactive change detection pipeline.
Using a struct as a configuration type will result in a runtime error.

## Example

### Non-compliant
```csharp
var config = manager.GetConfig<MyStruct>(); // MyStruct is a struct
```

### Compliant
```csharp
var config = manager.GetConfig<MyClass>(); // MyClass is a class
```

## How to fix

Change the configuration type from a struct to a class. If you need value semantics,
use a record class (`record`) instead of a record struct.
