using System.Runtime.CompilerServices;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Reactive;

[assembly: InternalsVisibleTo("Cocoar.Configuration.Tests")]
[assembly: InternalsVisibleTo("Cocoar.Configuration.Core.Tests")]
[assembly: InternalsVisibleTo("Cocoar.Configuration.DI")]
[assembly: InternalsVisibleTo("Cocoar.Configuration.Flags.Tests")]
[assembly: InternalsVisibleTo("Cocoar.Configuration.Secrets.Tests")]
[assembly: InternalsVisibleTo("Cocoar.Configuration.AspNetCore")]
[assembly: InternalsVisibleTo("Cocoar.Configuration.Http")]

// Type forwarding for types moved to Cocoar.Configuration.Abstractions
[assembly: TypeForwardedTo(typeof(IConfigurationAccessor))]
[assembly: TypeForwardedTo(typeof(IReactiveConfig<>))]
