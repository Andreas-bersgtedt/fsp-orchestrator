using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

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
        ["certificate", "ensure", var configurationPath] =>
            EnsureCertificate(configurationPath),
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

static int EnsureCertificate(string configurationPath)
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
        WriteCertificateConfiguration(configurationPath, existing.Thumbprint);
        return 0;
    }

    var keyCreation = new CngKeyCreationParameters
    {
        ExportPolicy = CngExportPolicies.None,
        KeyCreationOptions = CngKeyCreationOptions.MachineKey,
        KeyUsage = CngKeyUsages.AllUsages,
        Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
    };
    keyCreation.Parameters.Add(
        new CngProperty(
            "Length",
            BitConverter.GetBytes(3072),
            CngPropertyOptions.None));
    using var key = CngKey.Create(
        CngAlgorithm.Rsa,
        $"FspOrchestrator-{Guid.NewGuid():N}",
        keyCreation);
    using var rsa = new RSACng(key);
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

    using var certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddMinutes(-5),
        DateTimeOffset.UtcNow.AddYears(2));
    certificate.FriendlyName = certificateFriendlyName;
    personalStore.Add(certificate);
    EnsureTrusted(certificate);
    WriteCertificateConfiguration(configurationPath, certificate.Thumbprint);
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

static void WriteCertificateConfiguration(
    string configurationPath,
    string thumbprint)
{
    var configuration = new
    {
        Orchestrator = new
        {
            Certificate = new
            {
                Thumbprint = thumbprint,
                StoreLocation = StoreLocation.LocalMachine.ToString(),
            },
        },
    };
    var temporaryPath = $"{configurationPath}.{Guid.NewGuid():N}.tmp";
    try
    {
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(
                configuration,
                new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, configurationPath, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
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
