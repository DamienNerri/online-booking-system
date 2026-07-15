using Npgsql;

namespace OnlineBooking.Api.Data;

/// <summary>
/// Applique les scripts SQL de migration versionnés (dossier /migrations) de
/// façon idempotente et ordonnée. Chaque script appliqué est enregistré dans
/// la table schema_migrations. (Req 7.3 durabilité, Req 10 automatisation)
/// </summary>
public sealed class Migrator
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _migrationsDir;
    private readonly ILogger<Migrator> _logger;

    public Migrator(NpgsqlDataSource dataSource, string migrationsDir, ILogger<Migrator> logger)
    {
        _dataSource = dataSource;
        _migrationsDir = migrationsDir;
        _logger = logger;
    }

    // Clé arbitraire du verrou consultatif global de migration.
    private const long AdvisoryLockKey = 897654321L;

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Verrou consultatif : un seul nœud migre à la fois. Les autres attendent
        // puis constatent que les migrations sont déjà appliquées (Req 6/7 multi-nœuds).
        await using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.CommandText = "SELECT pg_advisory_lock(@k)";
            lockCmd.Parameters.AddWithValue("k", AdvisoryLockKey);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await RunMigrationsAsync(conn, ct);
        }
        finally
        {
            await using var unlock = conn.CreateCommand();
            unlock.CommandText = "SELECT pg_advisory_unlock(@k)";
            unlock.Parameters.AddWithValue("k", AdvisoryLockKey);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private async Task RunMigrationsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using (var create = conn.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    filename   TEXT PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                """;
            await create.ExecuteNonQueryAsync(ct);
        }

        if (!Directory.Exists(_migrationsDir))
        {
            _logger.LogWarning("Répertoire de migrations introuvable : {Dir}", _migrationsDir);
            return;
        }

        var files = Directory.GetFiles(_migrationsDir, "*.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (await IsAppliedAsync(conn, name, ct))
            {
                _logger.LogInformation("Migration déjà appliquée : {Name}", name);
                continue;
            }

            var sql = await File.ReadAllTextAsync(file, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var mark = conn.CreateCommand())
                {
                    mark.Transaction = tx;
                    mark.CommandText = "INSERT INTO schema_migrations (filename) VALUES (@f)";
                    mark.Parameters.AddWithValue("f", name);
                    await mark.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                _logger.LogInformation("Migration appliquée : {Name}", name);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
    }

    private static async Task<bool> IsAppliedAsync(NpgsqlConnection conn, string name, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM schema_migrations WHERE filename = @f";
        cmd.Parameters.AddWithValue("f", name);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }
}
