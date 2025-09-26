using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Infrastructure;

/// <summary>
/// Registry that manages type binding mappings, allowing interface types to resolve to concrete types.
/// </summary>
internal sealed class BindingRegistry
{
    private readonly Dictionary<Type, Type> _interfaceToConcreteMap = new();
    private readonly ILogger _logger;

    public BindingRegistry(IEnumerable<BindingSpec> bindings, ILogger logger)
    {
        _logger = logger;
        BuildMappingTable(bindings);
    }

    /// <summary>
    /// Tries to find the concrete type for the given interface type.
    /// </summary>
    /// <param name="interfaceType">The interface type to look up</param>
    /// <param name="concreteType">The mapped concrete type, if found</param>
    /// <returns>True if a mapping exists, false otherwise</returns>
    public bool TryGetConcreteType(Type interfaceType, out Type concreteType) => _interfaceToConcreteMap.TryGetValue(interfaceType, out concreteType!);

    /// <summary>
    /// Gets the concrete type for the given interface type, or returns the original type if no mapping exists.
    /// </summary>
    /// <param name="requestedType">The requested type (could be interface or concrete)</param>
    /// <returns>The concrete type to use for deserialization</returns>
    public Type GetConcreteTypeOrSelf(Type requestedType) =>
        _interfaceToConcreteMap.GetValueOrDefault(requestedType, requestedType);

    private void BuildMappingTable(IEnumerable<BindingSpec> bindings)
    {
        _interfaceToConcreteMap.Clear();
        
        foreach (var binding in bindings)
        {
            foreach (var interfaceType in binding.BoundInterfaces)
            {
                // Handle conflicts: last one wins (as requested)
                if (_interfaceToConcreteMap.TryGetValue(interfaceType, out var existingConcrete))
                {
                    _logger.LogWarning("Interface {InterfaceType} was already bound to {ExistingConcreteType}, now overridden by {NewConcreteType}",
                        interfaceType.Name, existingConcrete.Name, binding.ConcreteType.Name);
                }
                
                _interfaceToConcreteMap[interfaceType] = binding.ConcreteType;
                
                _logger.LogDebug("Bound interface {InterfaceType} → {ConcreteType}", 
                    interfaceType.Name, binding.ConcreteType.Name);
            }
        }
        
        _logger.LogInformation("Built binding registry with {Count} interface mappings", _interfaceToConcreteMap.Count);
    }
}
