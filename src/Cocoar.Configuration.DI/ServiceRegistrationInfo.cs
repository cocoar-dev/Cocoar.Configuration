using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;
public class ServiceRegistrationInfo
{
    public Type Type { get; set; }
    public bool DisableDefault { get; set; }
    

    public Dictionary<object, ServiceLifetime> ServiceLifetimes { get; } = new();
    public bool OverwriteDefault => ServiceLifetimes.ContainsKey("");
}
