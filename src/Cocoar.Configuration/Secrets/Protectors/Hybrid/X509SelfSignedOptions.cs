using System.Security.Cryptography.X509Certificates;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Defaults for creating and locating a self-signed X.509 certificate
/// used by the Hybrid (RSA+AES-GCM) protector for AutoProtect write scenarios.
/// </summary>
public sealed class X509SelfSignedCertificateOptions
{
    public string SubjectName { get; set; } = "CN=Cocoar.Configuration.AutoProtect";
    public int KeySize { get; set; } = 2048;
    public int ValidityYears { get; set; } = 5;
    public StoreLocation StoreLocation { get; set; } = StoreLocation.CurrentUser;
    public StoreName StoreName { get; set; } = StoreName.My;

}
