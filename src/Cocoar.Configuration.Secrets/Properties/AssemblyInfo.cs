using System.Runtime.CompilerServices;
using Cocoar.Configuration.Secrets.SecretTypes;

[assembly: InternalsVisibleTo("Cocoar.Configuration.Secrets.Tests")]

// Type forwarding for types moved to Cocoar.Configuration.Secrets.Abstractions
[assembly: TypeForwardedTo(typeof(SecretLease<>))]
