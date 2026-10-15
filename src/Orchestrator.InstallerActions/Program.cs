using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

const string certificateFriendlyName = "FSP Orchestrator localhost";
const string dataDirectoryName = "FspOrchestrator";

if (!OperatingSystem.IsWindows())
{
    return 1;
}

try
{
    return args switch
    {
        ["certificate", "ensure"] => EnsureCertificate(),
        ["certificate", "remove"] => RemoveCertificate(),
        ["data", "prompt-retention", var marker] => PromptForRetention(marker),
        ["data", "mark-removal", var marker] => MarkRemoval(marker),
        ["data", "remove-if-requested", var marker] =>
            RemoveDataIfRequested(marker),
        _ => 2,
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.GetType().Name);
    return 1;
}

static int EnsureCertificate()
{
    using var personalStore = new X509Store(
        StoreName.My,
        StoreLocation.LocalMachine);
    personalStore.Open(OpenFlags.ReadWrite);
    var existing = personalStore.Certificates
        .Where(certificate => string.Equals(
            certificate.FriendlyName,
            certificateFriendlyName,
            StringComparison.Ordinal))
        .OrderByDescending(certificate => certificate.NotAfter)
        .FirstOrDefault(IsUsable);
    if (existing is not null)
    {
        EnsureTrusted(existing);
        return 0;
    }

    using var rsa = RSA.Create(3072);
    var request = new CertificateRequest(
        "CN=localhost",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(
        new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
    request.CertificateExtensions.Add(
        new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));
    request.CertificateExtensions.Add(
        new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new("1.3.6.1.5.5.7.3.1"),
            },
            critical: true));
    var alternativeNames = new SubjectAlternativeNameBuilder();
    alternativeNames.AddDnsName("localhost");
    alternativeNames.AddIpAddress(System.Net.IPAddress.Loopback);
    alternativeNames.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
    request.CertificateExtensions.Add(alternativeNames.Build());

    using var generated = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddMinutes(-5),
        DateTimeOffset.UtcNow.AddYears(2));
    var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var pfx = generated.Export(X509ContentType.Pfx, password);
    using var persisted = X509CertificateLoader.LoadPkcs12(
        pfx,
        password,
        X509KeyStorageFlags.MachineKeySet
            | X509KeyStorageFlags.PersistKeySet);
    persisted.FriendlyName = certificateFriendlyName;
    personalStore.Add(persisted);
    EnsureTrusted(persisted);
    return 0;
}

static int RemoveCertificate()
{
    using var personalStore = new X509Store(
        StoreName.My,
        StoreLocation.LocalMachine);
    personalStore.Open(OpenFlags.ReadWrite);
    var certificates = personalStore.Certificates
        .Where(certificate => string.Equals(
            certificate.FriendlyName,
            certificateFriendlyName,
            StringComparison.Ordinal))
        .ToArray();
    foreach (var certificate in certificates)
    {
        using var privateKey = certificate.GetRSAPrivateKey();
        if (privateKey is RSACng cng
            && !string.IsNullOrWhiteSpace(cng.Key.UniqueName))
        {
            cng.Key.Delete();
        }

        personalStore.Remove(certificate);
        RemoveTrusted(certificate.Thumbprint);
        certificate.Dispose();
    }

    return 0;
}

static int PromptForRetention(string marker)
{
    const uint yesNo = 0x00000004;
    const uint question = 0x00000020;
    const uint defaultYes = 0x00000000;
    var response = MessageBox(
        IntPtr.Zero,
        "Retain local metadata, audit history, and identity configuration? "
            + "Choose Yes to retain data or No to remove it.",
        "FSP Orchestrator uninstall",
        yesNo | question | defaultYes);
    return response switch
    {
        6 => ClearRemovalMarker(marker),
        7 => MarkRemoval(marker),
        _ => 1,
    };
}

static int MarkRemoval(string marker)
{
    var directory = Path.GetDirectoryName(marker)
        ?? throw new InvalidOperationException(
            "The metadata removal marker directory is unavailable.");
    Directory.CreateDirectory(directory);
    File.WriteAllText(marker, "remove");
    return 0;
}

static int ClearRemovalMarker(string marker)
{
    if (File.Exists(marker))
    {
        File.Delete(marker);
    }

    return 0;
}

static int RemoveDataIfRequested(string marker)
{
    if (!File.Exists(marker))
    {
        return 0;
    }

    var dataDirectory = DataDirectory();
    File.Delete(marker);
    if (Directory.Exists(dataDirectory))
    {
        Directory.Delete(dataDirectory, recursive: true);
    }

    return 0;
}

static bool IsUsable(X509Certificate2 certificate)
{
    var now = DateTimeOffset.UtcNow;
    return certificate.HasPrivateKey
        && now >= certificate.NotBefore
        && now < certificate.NotAfter
        && string.Equals(
            certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false),
            "localhost",
            StringComparison.OrdinalIgnoreCase);
}

static void EnsureTrusted(X509Certificate2 certificate)
{
    using var rootStore = new X509Store(
        StoreName.Root,
        StoreLocation.LocalMachine);
    rootStore.Open(OpenFlags.ReadWrite);
    if (!rootStore.Certificates.Any(
            candidate => string.Equals(
                candidate.Thumbprint,
                certificate.Thumbprint,
                StringComparison.OrdinalIgnoreCase)))
    {
        rootStore.Add(
            X509CertificateLoader.LoadCertificate(
                certificate.Export(X509ContentType.Cert)));
    }
}

static void RemoveTrusted(string thumbprint)
{
    using var rootStore = new X509Store(
        StoreName.Root,
        StoreLocation.LocalMachine);
    rootStore.Open(OpenFlags.ReadWrite);
    var certificates = rootStore.Certificates.Find(
        X509FindType.FindByThumbprint,
        thumbprint,
        validOnly: false);
    foreach (var certificate in certificates)
    {
        rootStore.Remove(certificate);
        certificate.Dispose();
    }
}

static string DataDirectory() =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        dataDirectoryName);

[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern int MessageBox(
    IntPtr windowHandle,
    string text,
    string caption,
    uint type);
