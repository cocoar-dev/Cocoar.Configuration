using Microsoft.Extensions.Logging;
using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;

namespace Cocoar.Configuration.Infrastructure;


internal sealed class ExposureRegistry
{
    private readonly Dictionary<Type, Type> _interfaceToConcreteMap = new();
    private readonly Dictionary<Type, Type> _deserializationMap = new();
    private readonly ILogger _logger;
    private readonly CapabilityScope _capabilityScope;

    public ExposureRegistry(IEnumerable<SetupDefinition> bindings, ILogger logger, CapabilityScope capabilityScope)
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
        _interfaceToConcreteMap.Clear();
        _deserializationMap.Clear();
        
        foreach (var configureSpec in bindings)
        {

            if(!_capabilityScope.Compositions.TryGet(configureSpec, out var bag)){
                continue;
            }

           
            if (!bag.TryGetPrimaryAs<IPrimaryTypeCapability>(out var typeCapability))
            {
                _logger.LogDebug("ConfigureSpec does not have a valid primary type capability, skipping");
                continue;
            }

            var primaryType = typeCapability!.SelectedType;

            // Handle ConcreteType().ExposeAs<I>() - for DI exposure
            if (primaryType.IsClass)
            {
                var concreteType = primaryType;
                
                bag.GetAll<ExposeAsCapability<SetupDefinition>>().ForEach(exposeAs =>
                {
                    var interfaceType = exposeAs.ContractType;
                    

                    if (_interfaceToConcreteMap.TryGetValue(interfaceType, out var existingConcrete))
                    {
                        _logger.LogWarning("Interface {InterfaceType} was already exposed by {ExistingConcreteType}, now overridden by {NewConcreteType}",
                            interfaceType.Name, existingConcrete.Name, concreteType.Name);
                    }
                    
                    _interfaceToConcreteMap[interfaceType] = concreteType;
                    
                    _logger.LogDebug("Exposed interface {InterfaceType} → {ConcreteType}", 
                        interfaceType.Name, concreteType.Name);
                });
            }

            // Handle Interface<I>().DeserializeTo<T>() - for JSON deserialization
            if (primaryType.IsInterface)
            {
                var interfaceType = primaryType;
                
                var deserializeToCapability = bag.GetAll<DeserializeToCapability<SetupDefinition>>().FirstOrDefault();
                if (deserializeToCapability != null)
                {
                    var concreteType = deserializeToCapability.ConcreteType;

                    if (_deserializationMap.TryGetValue(interfaceType, out var existingConcrete))
                    {
                        _logger.LogWarning("Interface {InterfaceType} deserialization was already mapped to {ExistingConcreteType}, now overridden by {NewConcreteType}",
                            interfaceType.Name, existingConcrete.Name, concreteType.Name);
                    }
                    
                    _deserializationMap[interfaceType] = concreteType;
                    
                    _logger.LogDebug("Interface deserialization mapping: {InterfaceType} → {ConcreteType}", 
                        interfaceType.Name, concreteType.Name);
                }
            }
        }
        
        _logger.LogInformation("Built exposure registry with {ExposureCount} DI mappings and {DeserializationCount} deserialization mappings", 
            _interfaceToConcreteMap.Count, _deserializationMap.Count);
    }
}
