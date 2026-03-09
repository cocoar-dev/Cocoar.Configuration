using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Infrastructure;


internal static partial class ExposureRegistryLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug, Message = "ConfigureSpec does not have a valid primary type capability, skipping")]
    public static partial void MissingPrimaryTypeCapability(this ILogger logger);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Interface {InterfaceType} was already exposed by {ExistingConcreteType}, now overridden by {NewConcreteType}")]
    public static partial void ExposeOverride(this ILogger logger, string InterfaceType, string ExistingConcreteType, string NewConcreteType);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "Exposed interface {InterfaceType} → {ConcreteType}")]
    public static partial void ExposedInterface(this ILogger logger, string InterfaceType, string ConcreteType);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Interface {InterfaceType} deserialization was already mapped to {ExistingConcreteType}, now overridden by {NewConcreteType}")]
    public static partial void DeserializeMappingOverride(this ILogger logger, string InterfaceType, string ExistingConcreteType, string NewConcreteType);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Debug, Message = "Interface deserialization mapping: {InterfaceType} → {ConcreteType}")]
    public static partial void DeserializeMapping(this ILogger logger, string InterfaceType, string ConcreteType);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "Built exposure registry with {ExposureCount} DI mappings and {DeserializationCount} deserialization mappings")]
    public static partial void BuiltExposureRegistry(this ILogger logger, int ExposureCount, int DeserializationCount);
}

internal sealed class ExposureRegistry
{
    private FrozenDictionary<Type, Type> _interfaceToConcreteMap = FrozenDictionary<Type, Type>.Empty;
    private FrozenDictionary<Type, Type> _deserializationMap = FrozenDictionary<Type, Type>.Empty;
    private readonly ILogger _logger;
    private readonly ConfigManagerCapabilityScope _capabilityScope;

    public ExposureRegistry(IEnumerable<SetupDefinition> bindings, ILogger logger, ConfigManagerCapabilityScope capabilityScope)
    {
        _logger = logger;
        _capabilityScope = capabilityScope;
        BuildMappingTables(bindings);
    }

    public bool TryGetConcreteType(Type interfaceType, out Type concreteType) => _interfaceToConcreteMap.TryGetValue(interfaceType, out concreteType!);

    public IReadOnlyDictionary<Type, Type> InterfaceToConcreteMap => _interfaceToConcreteMap;

    /// <summary>
    /// Gets the deserialization mappings for interfaces to concrete types.
    /// This is separate from the DI exposure mappings and is specifically for JSON deserialization.
    /// </summary>
    public IReadOnlyDictionary<Type, Type> DeserializationMap => _deserializationMap;


    private void BuildMappingTables(IEnumerable<SetupDefinition> bindings)
    {
        var interfaceToConcreteMap = new Dictionary<Type, Type>();
        var deserializationMap = new Dictionary<Type, Type>();

        foreach (var configureSpec in bindings)
        {

            if(!_capabilityScope.Compositions.TryGet(configureSpec, out var bag)){
                continue;
            }


            if (!bag.TryGetPrimaryAs<IPrimaryTypeCapability>(out var typeCapability))
            {
                _logger.MissingPrimaryTypeCapability();
                continue;
            }

            var primaryType = typeCapability!.SelectedType;
            if (primaryType.IsClass)
            {
                var concreteType = primaryType;

                bag.GetAll<ExposeAsCapability<SetupDefinition>>().ForEach(exposeAs =>
                {
                    var interfaceType = exposeAs.ContractType;


                    if (interfaceToConcreteMap.TryGetValue(interfaceType, out var existingConcrete))
                    {
                        _logger.ExposeOverride(interfaceType.Name, existingConcrete.Name, concreteType.Name);
                    }

                    interfaceToConcreteMap[interfaceType] = concreteType;

                    _logger.ExposedInterface(interfaceType.Name, concreteType.Name);
                });
            }
            if (primaryType.IsInterface)
            {
                var interfaceType = primaryType;

                var deserializeCaps = bag.GetAll<DeserializeToCapability<SetupDefinition>>();
                var deserializeToCapability = deserializeCaps.Count != 0 ? deserializeCaps[0] : null;
                if (deserializeToCapability != null)
                {
                    var concreteType = deserializeToCapability.ConcreteType;

                    if (deserializationMap.TryGetValue(interfaceType, out var existingConcrete))
                    {
                        _logger.DeserializeMappingOverride(interfaceType.Name, existingConcrete.Name, concreteType.Name);
                    }

                    deserializationMap[interfaceType] = concreteType;

                    _logger.DeserializeMapping(interfaceType.Name, concreteType.Name);
                }
            }
        }

        _interfaceToConcreteMap = interfaceToConcreteMap.ToFrozenDictionary();
        _deserializationMap = deserializationMap.ToFrozenDictionary();

        _logger.BuiltExposureRegistry(_interfaceToConcreteMap.Count, _deserializationMap.Count);
    }
}
