using Microsoft.Extensions.Logging;
using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;

namespace Cocoar.Configuration.Infrastructure;


internal sealed class ExposureRegistry
{
    private readonly Dictionary<Type, Type> _interfaceToConcreteMap = new();
    private readonly ILogger _logger;
    private readonly CapabilityScope _capabilityScope;

    public ExposureRegistry(IEnumerable<SetupDefinition> bindings, ILogger logger, CapabilityScope capabilityScope)
    {
        _logger = logger;
        _capabilityScope = capabilityScope;
        BuildMappingTable(bindings);
    }

    public bool TryGetConcreteType(Type interfaceType, out Type concreteType) => _interfaceToConcreteMap.TryGetValue(interfaceType, out concreteType!);


    private void BuildMappingTable(IEnumerable<SetupDefinition> bindings)
    {
        _interfaceToConcreteMap.Clear();
        
        foreach (var configureSpec in bindings)
        {

            if(!_capabilityScope.Compositions.TryGet(configureSpec, out var bag)){
                continue;
            }

           
            if (!bag.TryGetPrimaryAs<IPrimaryTypeCapability>(out var typeCapability))
            {
                _logger.LogDebug("ConfigureSpec does not have a valid primary type capability, skipping exposure registration");
                continue;
            }

            var concreteType = typeCapability!.SelectedType;

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
        
        _logger.LogInformation("Built exposure registry with {Count} interface mappings", _interfaceToConcreteMap.Count);
    }
}
