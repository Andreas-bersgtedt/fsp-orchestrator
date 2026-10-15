using System.Security.Cryptography;
using Fsp.Orchestrator.Application.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fsp.Orchestrator.Infrastructure.Persistence;

internal sealed class DatabaseMaintenance(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    string databasePath)
    : IDatabaseMaintenance, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string backupDirectory = Path.Combine(
        Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Database directory is unavailable."),
        "backups");

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(
                cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "PRAGMA journal_mode=WAL;",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "PRAGMA synchronous=FULL;",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "PRAGMA foreign_keys=ON;",
                cancellationToken);
            var integrity = await IntegrityAsync(
                context.Database.GetDbConnection().ConnectionString,
                cancellationToken);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SQLite integrity check failed: {integrity}");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DatabaseDiagnostics> GetDiagnosticsAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(
                cancellationToken);
            var connectionString =
                context.Database.GetDbConnection().ConnectionString;
            var integrity = await IntegrityAsync(
                connectionString,
                cancellationToken);
            var migration = (await context.Database.GetAppliedMigrationsAsync(
                    cancellationToken))
                .LastOrDefault() ?? "none";
            var pendingMigrationCount = (await context.Database.GetPendingMigrationsAsync(
                    cancellationToken))
                .Count();
            return new DatabaseDiagnostics(
                Path.GetFileName(databasePath),
                integrity,
                migration,
                pendingMigrationCount,
                await context.Deployments.LongCountAsync(cancellationToken),
                await context.Jobs.LongCountAsync(cancellationToken),
                await context.AuditEvents.LongCountAsync(cancellationToken),
                DateTimeOffset.UtcNow);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DatabaseBackupInfo> CreateBackupAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(backupDirectory);
            var createdAt = DateTimeOffset.UtcNow;
            var fileName =
                $"orchestrator-{createdAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db";
            var backupPath = Path.Combine(backupDirectory, fileName);
            await BackupAsync(databasePath, backupPath, cancellationToken);
            var integrity = await IntegrityAsync(
                ConnectionString(backupPath, SqliteOpenMode.ReadOnly),
                cancellationToken);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(backupPath);
                throw new InvalidOperationException(
                    $"SQLite backup integrity check failed: {integrity}");
            }

            var bytes = await File.ReadAllBytesAsync(
                backupPath,
                cancellationToken);
            return new DatabaseBackupInfo(
                fileName,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                bytes.LongLength,
                createdAt);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RestoreBackupAsync(
        string fileName,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                fileName,
                Path.GetFileName(fileName),
                StringComparison.Ordinal)
            || !fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Backup file name is invalid.", nameof(fileName));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var backupPath = Path.Combine(backupDirectory, fileName);
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException(
                    "The requested database backup was not found.",
                    fileName);
            }

            var bytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
            var actualHash = SHA256.HashData(bytes);
            byte[] expectedHash;
            try
            {
                expectedHash = Convert.FromHexString(expectedSha256);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException(
                    "Expected SHA-256 must be hexadecimal.",
                    nameof(expectedSha256),
                    exception);
            }

            if (expectedHash.Length != actualHash.Length
                || !CryptographicOperations.FixedTimeEquals(
                    expectedHash,
                    actualHash))
            {
                throw new InvalidOperationException(
                    "Database backup hash does not match the approved backup.");
            }

            var integrity = await IntegrityAsync(
                ConnectionString(backupPath, SqliteOpenMode.ReadOnly),
                cancellationToken);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SQLite backup integrity check failed: {integrity}");
            }

            SqliteConnection.ClearAllPools();
            await BackupAsync(backupPath, databasePath, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        gate.Dispose();
    }

    private static async Task BackupAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 5;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                await BackupOnceAsync(
                    sourcePath,
                    destinationPath,
                    cancellationToken);
                return;
            }
            catch (SqliteException exception)
                when (exception.SqliteErrorCode is 5 or 6
                    && attempt < maximumAttempts)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(50 * attempt),
                    cancellationToken);
            }
        }
    }

    private static async Task BackupOnceAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(
            ConnectionString(sourcePath, SqliteOpenMode.ReadOnly));
        await using var destination = new SqliteConnection(
            ConnectionString(destinationPath, SqliteOpenMode.ReadWriteCreate));
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static async Task<string> IntegrityAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture)
            ?? "unknown";
    }

    private static string ConnectionString(
        string path,
        SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            Mode = mode,
            Pooling = false,
        }.ToString();
}
