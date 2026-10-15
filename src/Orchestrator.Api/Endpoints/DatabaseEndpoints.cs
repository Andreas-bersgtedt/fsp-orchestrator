using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Application.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class DatabaseEndpoints
{
    public static RouteGroupBuilder MapDatabase(this RouteGroupBuilder endpoints)
    {
        endpoints.MapGet(
                "/system/database",
                async Task<Ok<DatabaseDiagnosticsResponse>> (
                    IDatabaseMaintenance maintenance,
                    CancellationToken cancellationToken) =>
                {
                    var diagnostics = await maintenance.GetDiagnosticsAsync(
                        cancellationToken);
                    return TypedResults.Ok(
                        new DatabaseDiagnosticsResponse(
                            diagnostics.DatabaseFileName,
                            diagnostics.Integrity,
                            diagnostics.Migration,
                            diagnostics.PendingMigrationCount,
                            diagnostics.DeploymentCount,
                            diagnostics.JobCount,
                            diagnostics.AuditEventCount,
                            diagnostics.CheckedAt));
                })
            .RequireAuthorization(AuthorizationPolicies.DatabaseDiagnostics)
            .WithName("GetDatabaseDiagnostics")
            .WithSummary("Get local database diagnostics")
            .WithDescription(
                "Returns migration, integrity, and non-secret record counts.")
            .Produces<DatabaseDiagnosticsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPost(
                "/system/database/backups",
                async Task<Created<DatabaseBackupResponse>> (
                    IDatabaseMaintenance maintenance,
                    IAuditMetadataStore auditStore,
                    ClaimsPrincipal user,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var backup = await maintenance.CreateBackupAsync(
                        cancellationToken);
                    await auditStore.AppendAsync(
                        Audit(
                            user,
                            httpContext,
                            "database.backup",
                            backup.FileName,
                            "succeeded",
                            backup.Sha256),
                        cancellationToken);
                    return TypedResults.Created(
                        "/api/system/database",
                        new DatabaseBackupResponse(
                            backup.FileName,
                            backup.Sha256,
                            backup.Length,
                            backup.CreatedAt));
                })
            .RequireAuthorization(OrchestratorRoles.SecurityAdmin)
            .WithName("CreateDatabaseBackup")
            .WithSummary("Create a local metadata backup")
            .WithDescription(
                "Creates and integrity-checks an online SQLite backup without secret values.")
            .Produces<DatabaseBackupResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPost(
                "/system/database/restore",
                RestoreAsync)
            .RequireAuthorization(OrchestratorRoles.SecurityAdmin)
            .WithName("RestoreDatabaseBackup")
            .WithSummary("Restore a local metadata backup")
            .WithDescription(
                "Restores an approved backup only after SHA-256 and SQLite integrity checks.")
            .Produces<DatabaseRestoreResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<Results<
        Ok<DatabaseRestoreResponse>,
        BadRequest<ProblemDetails>,
        NotFound<ProblemDetails>,
        Conflict<ProblemDetails>>> RestoreAsync(
        RestoreDatabaseBackupRequest request,
        IDatabaseMaintenance maintenance,
        IAuditMetadataStore auditStore,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await maintenance.RestoreBackupAsync(
                request.FileName,
                request.Sha256,
                cancellationToken);
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest(
                Problem(
                    StatusCodes.Status400BadRequest,
                    "Invalid backup request",
                    "Backup file name and SHA-256 must use the documented format."));
        }
        catch (FileNotFoundException)
        {
            return TypedResults.NotFound(
                Problem(
                    StatusCodes.Status404NotFound,
                    "Backup not found",
                    "The requested backup does not exist in the Orchestrator backup directory."));
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Conflict(
                Problem(
                    StatusCodes.Status409Conflict,
                    "Backup validation failed",
                    "The backup hash or SQLite integrity check failed."));
        }

        await auditStore.AppendAsync(
            Audit(
                user,
                httpContext,
                "database.restore",
                request.FileName,
                "succeeded",
                request.Sha256),
            cancellationToken);
        return TypedResults.Ok(
            new DatabaseRestoreResponse(
                true,
                request.FileName,
                DateTimeOffset.UtcNow));
    }

    private static AppendAuditMetadata Audit(
        ClaimsPrincipal user,
        HttpContext httpContext,
        string action,
        string targetId,
        string result,
        string input)
    {
        return new AppendAuditMetadata(
            DateTimeOffset.UtcNow,
            user.FindFirstValue("oid") ?? string.Empty,
            user.FindFirstValue("tid") ?? string.Empty,
            action,
            "orchestrator.database",
            targetId,
            result,
            httpContext.TraceIdentifier,
            Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(input)))
                .ToLowerInvariant());
    }

    private static ProblemDetails Problem(
        int status,
        string title,
        string detail) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail,
        };
}

/// <summary>Current non-secret state of the local Orchestrator database.</summary>
public sealed record DatabaseDiagnosticsResponse(
    string DatabaseFileName,
    string Integrity,
    string Migration,
    int PendingMigrationCount,
    long DeploymentCount,
    long JobCount,
    long AuditEventCount,
    DateTimeOffset CheckedAt);

/// <summary>Metadata needed to approve and later restore a database backup.</summary>
public sealed record DatabaseBackupResponse(
    string FileName,
    string Sha256,
    long Length,
    DateTimeOffset CreatedAt);

/// <summary>Payload approving one known database backup for restore.</summary>
public sealed record RestoreDatabaseBackupRequest
{
    public required string FileName { get; init; }

    public required string Sha256 { get; init; }
}

/// <summary>Result of restoring a validated local metadata backup.</summary>
public sealed record DatabaseRestoreResponse(
    bool Restored,
    string FileName,
    DateTimeOffset RestoredAt);
