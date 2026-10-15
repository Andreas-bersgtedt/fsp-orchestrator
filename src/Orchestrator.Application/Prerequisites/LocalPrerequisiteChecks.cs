namespace Fsp.Orchestrator.Application.Prerequisites;

public static class PrerequisiteSystemInputKeys
{
    public const string CurrentTenantId = "$orchestrator.currentTenantId";
    public const string CurrentRoles = "$orchestrator.currentRoles";
    public const string CurrentClientId = "$orchestrator.currentClientId";
    public const string CurrentScopes = "$orchestrator.currentScopes";
}

public interface ILocalPrerequisiteProbe
{
    LocalHostSnapshot GetHost();

    Task<bool> IsLoopbackListenerAvailableAsync(CancellationToken cancellationToken);

    LocalCertificateSnapshot GetCertificate();

    Task<LocalDatabaseSnapshot> GetDatabaseAsync(CancellationToken cancellationToken);

    Task<LocalClockSnapshot> GetClockAsync(CancellationToken cancellationToken);

    LocalEntraSnapshot GetEntra();
}

public sealed record LocalHostSnapshot(bool IsWindows, bool IsWindowsService);

public sealed record LocalCertificateSnapshot(
    string Subject,
    DateTimeOffset NotAfter);

public sealed record LocalDatabaseSnapshot(
    string Integrity,
    string Migration,
    int PendingMigrationCount,
    long AvailableBytes);

public sealed record LocalClockSnapshot(
    string Source,
    TimeSpan Skew);

public sealed record LocalEntraSnapshot(
    string TenantId,
    string ApiClientId,
    string SpaClientId,
    string RedirectUri,
    string ScopeName,
    string ApiScope);

public static class LocalPrerequisiteCatalog
{
    private static readonly string[] Operations =
        ["setup", "register", "deploy", "configure"];
    private static readonly string[] IdentityInputKeys =
    [
        PrerequisiteSystemInputKeys.CurrentTenantId,
        PrerequisiteSystemInputKeys.CurrentRoles,
        PrerequisiteSystemInputKeys.CurrentClientId,
        PrerequisiteSystemInputKeys.CurrentScopes,
    ];

    public static IReadOnlyList<PrerequisiteCheckDefinition> Definitions { get; } =
    [
        Definition(
            "local.windows-host",
            "Local host",
            "Windows host",
            "Verifies that Orchestrator is running on a supported Windows host.",
            blocking: true),
        Definition(
            "local.windows-service",
            "Local host",
            "Windows Service",
            "Reports whether Orchestrator is hosted as a Windows Service.",
            dependencies: ["local.windows-host"],
            blocking: false),
        Definition(
            "local.loopback-listener",
            "Local host",
            "Loopback HTTPS listener",
            "Verifies that the fixed loopback listener is reachable.",
            dependencies: ["local.windows-host"],
            blocking: true),
        Definition(
            "local.https-certificate",
            "Local host",
            "Local HTTPS certificate",
            "Checks the localhost certificate subject and remaining lifetime.",
            dependencies: ["local.loopback-listener"],
            blocking: true,
            lifetime: TimeSpan.FromHours(1)),
        Definition(
            "local.database",
            "Local host",
            "SQLite database",
            "Checks migration state, integrity, and available disk space.",
            dependencies: ["local.windows-host"],
            blocking: true,
            lifetime: TimeSpan.FromMinutes(5)),
        Definition(
            "local.clock",
            "Local host",
            "System clock",
            "Measures clock skew against a bounded trusted HTTPS time source.",
            blocking: true,
            timeout: TimeSpan.FromSeconds(10),
            lifetime: TimeSpan.FromMinutes(15)),
        Definition(
            "entra.tenant",
            "Entra",
            "Entra tenant",
            "Checks the configured tenant against the authenticated tenant.",
            blocking: true,
            inputKeys: IdentityInputKeys),
        Definition(
            "entra.client-ids",
            "Entra",
            "SPA and API client IDs",
            "Checks that distinct valid SPA and API application IDs are configured.",
            blocking: true),
        Definition(
            "entra.redirect-uri",
            "Entra",
            "SPA redirect URI",
            "Checks the configured SPA redirect URI.",
            blocking: true),
        Definition(
            "entra.delegated-scope",
            "Entra",
            "Delegated API scope",
            "Checks the configured delegated API scope.",
            dependencies: ["entra.client-ids"],
            blocking: true,
            inputKeys: IdentityInputKeys),
        Definition(
            "entra.authorized-client",
            "Entra",
            "Authorized SPA client",
            "Checks that the API authorization boundary uses the configured SPA.",
            dependencies: ["entra.client-ids"],
            blocking: true,
            inputKeys: IdentityInputKeys),
        Definition(
            "entra.current-role",
            "Entra",
            "Current role assignment",
            "Checks that the authenticated user has an Orchestrator application role.",
            dependencies: ["entra.tenant"],
            blocking: true,
            inputKeys: IdentityInputKeys),
    ];

    private static PrerequisiteCheckDefinition Definition(
        string id,
        string category,
        string displayName,
        string description,
        bool blocking,
        IReadOnlyList<string>? dependencies = null,
        IReadOnlyList<string>? inputKeys = null,
        TimeSpan? timeout = null,
        TimeSpan? lifetime = null) =>
        new(
            id,
            category,
            displayName,
            description,
            dependencies ?? [],
            Operations,
            inputKeys ?? [],
            timeout ?? TimeSpan.FromSeconds(5),
            lifetime ?? TimeSpan.FromMinutes(30),
            blocking,
            "1.0");
}

public sealed class LocalPrerequisiteCheck(
    PrerequisiteCheckDefinition definition,
    ILocalPrerequisiteProbe probe)
    : IPrerequisiteCheck
{
    private static readonly IReadOnlyDictionary<string, string> EmptyEvidence =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly HashSet<string> Roles =
    [
        "Reader",
        "Operator",
        "ConfigurationAdmin",
        "DeploymentAdmin",
        "SecurityAdmin",
    ];

    public PrerequisiteCheckDefinition Definition { get; } = definition;

    public async Task<PrerequisiteCheckOutcome> ExecuteAsync(
        PrerequisiteCheckContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Definition.Id switch
        {
            "local.windows-host" => WindowsHost(),
            "local.windows-service" => WindowsService(),
            "local.loopback-listener" => await LoopbackListenerAsync(cancellationToken),
            "local.https-certificate" => Certificate(),
            "local.database" => await DatabaseAsync(cancellationToken),
            "local.clock" => await ClockAsync(cancellationToken),
            "entra.tenant" => Tenant(context),
            "entra.client-ids" => ClientIds(),
            "entra.redirect-uri" => RedirectUri(),
            "entra.delegated-scope" => DelegatedScope(context),
            "entra.authorized-client" => AuthorizedClient(context),
            "entra.current-role" => CurrentRole(context),
            _ => throw new InvalidOperationException(
                $"Unsupported built-in prerequisite check '{Definition.Id}'."),
        };
    }

    private PrerequisiteCheckOutcome WindowsHost()
    {
        var host = probe.GetHost();
        return host.IsWindows
            ? Passed(
                "local.windows-host.passed",
                "The host operating system is supported.",
                new Dictionary<string, string> { ["platform"] = "Windows" })
            : Failed(
                "local.windows-host.unsupported",
                "The host operating system is not Windows.",
                "Run FSP Orchestrator on a supported Windows host.",
                new Dictionary<string, string> { ["field"] = "operatingSystem" });
    }

    private PrerequisiteCheckOutcome WindowsService()
    {
        var host = probe.GetHost();
        return host.IsWindowsService
            ? Passed(
                "local.windows-service.passed",
                "Orchestrator is running as a Windows Service.")
            : Warning(
                "local.windows-service.interactive",
                "Orchestrator is running interactively.",
                "Install and start the FSP Orchestrator Windows Service before production use.");
    }

    private async Task<PrerequisiteCheckOutcome> LoopbackListenerAsync(
        CancellationToken cancellationToken)
    {
        return await probe.IsLoopbackListenerAvailableAsync(cancellationToken)
            ? Passed(
                "local.loopback-listener.passed",
                "The loopback HTTPS listener is reachable.",
                new Dictionary<string, string>
                {
                    ["origin"] = "https://localhost:9443",
                })
            : Failed(
                "local.loopback-listener.unavailable",
                "The fixed loopback listener is unavailable.",
                "Start Orchestrator on https://localhost:9443 and verify the port is not blocked.");
    }

    private PrerequisiteCheckOutcome Certificate()
    {
        var certificate = probe.GetCertificate();
        var remaining = certificate.NotAfter - DateTimeOffset.UtcNow;
        var evidence = new Dictionary<string, string>
        {
            ["subject"] = certificate.Subject,
            ["expiresAt"] = certificate.NotAfter.ToString("O"),
            ["remainingDays"] = Math.Floor(remaining.TotalDays).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        };
        if (remaining <= TimeSpan.Zero)
        {
            return Failed(
                "local.https-certificate.expired",
                "The localhost certificate has expired.",
                "Install a valid localhost certificate and restart Orchestrator.",
                evidence);
        }

        return remaining < TimeSpan.FromDays(30)
            ? Warning(
                "local.https-certificate.expiring",
                "The localhost certificate expires within 30 days.",
                "Renew the localhost certificate before it expires.",
                evidence)
            : Passed(
                "local.https-certificate.passed",
                "The localhost certificate is valid.",
                evidence);
    }

    private async Task<PrerequisiteCheckOutcome> DatabaseAsync(
        CancellationToken cancellationToken)
    {
        var database = await probe.GetDatabaseAsync(cancellationToken);
        var evidence = new Dictionary<string, string>
        {
            ["integrity"] = database.Integrity,
            ["migration"] = database.Migration,
            ["pendingMigrations"] = database.PendingMigrationCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["availableBytes"] = database.AvailableBytes.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        };
        if (!string.Equals(database.Integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                "local.database.integrity_failed",
                "SQLite integrity validation failed.",
                "Restore a verified backup before continuing.",
                evidence);
        }

        if (database.PendingMigrationCount > 0)
        {
            return Failed(
                "local.database.migrations_pending",
                "SQLite has pending migrations.",
                "Restart Orchestrator and allow database migration to complete.",
                evidence);
        }

        return database.AvailableBytes < 1_073_741_824
            ? Warning(
                "local.database.low_space",
                "The database volume has less than 1 GiB available.",
                "Free disk space on the Orchestrator data volume.",
                evidence)
            : Passed(
                "local.database.passed",
                "SQLite migration, integrity, and free-space checks passed.",
                evidence);
    }

    private async Task<PrerequisiteCheckOutcome> ClockAsync(
        CancellationToken cancellationToken)
    {
        var clock = await probe.GetClockAsync(cancellationToken);
        var absoluteSkew = clock.Skew.Duration();
        var evidence = new Dictionary<string, string>
        {
            ["source"] = clock.Source,
            ["skewMilliseconds"] = Math.Round(clock.Skew.TotalMilliseconds).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        };
        if (absoluteSkew > TimeSpan.FromMinutes(5))
        {
            return Failed(
                "local.clock.skew_excessive",
                "System clock skew exceeds five minutes.",
                "Synchronize the Windows clock before authenticating.",
                evidence);
        }

        return absoluteSkew > TimeSpan.FromSeconds(30)
            ? Warning(
                "local.clock.skew_detected",
                "System clock skew exceeds 30 seconds.",
                "Synchronize the Windows clock to avoid token validation failures.",
                evidence)
            : Passed(
                "local.clock.passed",
                "System clock skew is within tolerance.",
                evidence);
    }

    private PrerequisiteCheckOutcome Tenant(PrerequisiteCheckContext context)
    {
        var entra = probe.GetEntra();
        context.Inputs.TryGetValue(
            PrerequisiteSystemInputKeys.CurrentTenantId,
            out var currentTenantId);
        var evidence = new Dictionary<string, string>
        {
            ["field"] = "tenantId",
            ["configured"] = entra.TenantId,
            ["authenticated"] = currentTenantId ?? string.Empty,
        };
        return Guid.TryParse(entra.TenantId, out _)
            && string.Equals(
                entra.TenantId,
                currentTenantId,
                StringComparison.OrdinalIgnoreCase)
            ? Passed(
                "entra.tenant.passed",
                "The configured tenant matches the authenticated tenant.",
                evidence)
            : Failed(
                "entra.tenant.mismatch",
                "The Entra tenantId field does not match the authenticated tenant.",
                "Apply an identity configuration for the authenticated tenant.",
                evidence);
    }

    private PrerequisiteCheckOutcome ClientIds()
    {
        var entra = probe.GetEntra();
        var apiValid = Guid.TryParse(entra.ApiClientId, out _);
        var spaValid = Guid.TryParse(entra.SpaClientId, out _);
        var distinct = !string.Equals(
            entra.ApiClientId,
            entra.SpaClientId,
            StringComparison.OrdinalIgnoreCase);
        var evidence = new Dictionary<string, string>
        {
            ["field"] = !apiValid
                ? "apiClientId"
                : !spaValid
                    ? "spaClientId"
                    : "spaClientId/apiClientId",
            ["apiClientId"] = entra.ApiClientId,
            ["spaClientId"] = entra.SpaClientId,
        };
        return apiValid && spaValid && distinct
            ? Passed(
                "entra.client-ids.passed",
                "Distinct valid SPA and API client IDs are configured.",
                evidence)
            : Failed(
                "entra.client-ids.invalid",
                $"The Entra {evidence["field"]} field is invalid.",
                "Select distinct valid SPA and API application registrations.",
                evidence);
    }

    private PrerequisiteCheckOutcome RedirectUri()
    {
        var entra = probe.GetEntra();
        var evidence = new Dictionary<string, string>
        {
            ["field"] = "redirectUri",
            ["configured"] = entra.RedirectUri,
            ["expected"] = "https://localhost:9443/auth/callback",
        };
        return string.Equals(
            entra.RedirectUri,
            evidence["expected"],
            StringComparison.Ordinal)
            ? Passed(
                "entra.redirect-uri.passed",
                "The SPA redirectUri field matches the loopback callback.",
                evidence)
            : Failed(
                "entra.redirect-uri.mismatch",
                "The Entra redirectUri field is mismatched.",
                $"Configure the SPA redirect URI as {evidence["expected"]}.",
                evidence);
    }

    private PrerequisiteCheckOutcome DelegatedScope(
        PrerequisiteCheckContext context)
    {
        var entra = probe.GetEntra();
        context.Inputs.TryGetValue(
            PrerequisiteSystemInputKeys.CurrentScopes,
            out var currentScopes);
        var expected = $"api://{entra.ApiClientId}/orchestrator.access_as_user";
        var tokenHasScope = (currentScopes ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("orchestrator.access_as_user", StringComparer.Ordinal);
        var evidence = new Dictionary<string, string>
        {
            ["field"] = "delegatedScope",
            ["configured"] = entra.ApiScope,
            ["expected"] = expected,
        };
        return string.Equals(entra.ScopeName, "orchestrator.access_as_user", StringComparison.Ordinal)
            && string.Equals(entra.ApiScope, expected, StringComparison.Ordinal)
            && tokenHasScope
            ? Passed(
                "entra.delegated-scope.passed",
                "The delegatedScope field matches the Orchestrator contract.",
                evidence)
            : Failed(
                "entra.delegated-scope.mismatch",
                "The Entra delegatedScope field is mismatched.",
                "Expose and configure orchestrator.access_as_user on the selected API.",
                evidence);
    }

    private PrerequisiteCheckOutcome AuthorizedClient(
        PrerequisiteCheckContext context)
    {
        var entra = probe.GetEntra();
        context.Inputs.TryGetValue(
            PrerequisiteSystemInputKeys.CurrentClientId,
            out var currentClientId);
        var evidence = new Dictionary<string, string>
        {
            ["field"] = "authorizedSpaClientId",
            ["configured"] = entra.SpaClientId,
            ["authenticated"] = currentClientId ?? string.Empty,
        };
        return Guid.TryParse(entra.SpaClientId, out _)
            && string.Equals(
                entra.SpaClientId,
                currentClientId,
                StringComparison.OrdinalIgnoreCase)
            ? Passed(
                "entra.authorized-client.passed",
                "The authorizedSpaClientId field is configured for token validation.",
                evidence)
            : Failed(
                "entra.authorized-client.mismatch",
                "The Entra authorizedSpaClientId field is invalid.",
                "Apply a validated SPA/API registration pair.",
                evidence);
    }

    private static PrerequisiteCheckOutcome CurrentRole(
        PrerequisiteCheckContext context)
    {
        context.Inputs.TryGetValue(
            PrerequisiteSystemInputKeys.CurrentRoles,
            out var currentRoles);
        var assignedRoles = (currentRoles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var evidence = new Dictionary<string, string>
        {
            ["field"] = "currentRoleAssignment",
            ["assignedRoles"] = string.Join(",", assignedRoles.Order(StringComparer.Ordinal)),
        };
        return assignedRoles.Any(Roles.Contains)
            ? Passed(
                "entra.current-role.passed",
                "The currentRoleAssignment field contains an Orchestrator role.",
                evidence)
            : Failed(
                "entra.current-role.missing",
                "The Entra currentRoleAssignment field has no Orchestrator role.",
                "Assign at least one Orchestrator API application role to the current user.",
                evidence);
    }

    private static PrerequisiteCheckOutcome Passed(
        string code,
        string summary,
        IReadOnlyDictionary<string, string>? evidence = null) =>
        new(
            PrerequisiteCheckStatus.Passed,
            code,
            summary,
            evidence ?? EmptyEvidence,
            string.Empty);

    private static PrerequisiteCheckOutcome Warning(
        string code,
        string summary,
        string remediation,
        IReadOnlyDictionary<string, string>? evidence = null) =>
        new(
            PrerequisiteCheckStatus.Warning,
            code,
            summary,
            evidence ?? EmptyEvidence,
            remediation);

    private static PrerequisiteCheckOutcome Failed(
        string code,
        string summary,
        string remediation,
        IReadOnlyDictionary<string, string>? evidence = null) =>
        new(
            PrerequisiteCheckStatus.Failed,
            code,
            summary,
            evidence ?? EmptyEvidence,
            remediation);
}
