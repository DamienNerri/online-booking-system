using Npgsql;
using OnlineBooking.Api.Models;

namespace OnlineBooking.Api.Repositories;

/// <summary>Accès aux comptes utilisateurs (requêtes paramétrées, Req 9.2).</summary>
public sealed class UserRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public UserRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<User?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        const string sql = "SELECT id, email, password_hash, role FROM users WHERE email = @e;";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("e", email);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
        {
            return null;
        }
        return new User(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3));
    }

    /// <summary>Crée un utilisateur. Retourne null si l'email existe déjà.</summary>
    public async Task<User?> CreateAsync(string email, string passwordHash, string role, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO users (email, password_hash, role)
            VALUES (@e, @h, @r)
            ON CONFLICT (email) DO NOTHING
            RETURNING id, email, password_hash, role;
            """;
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("e", email);
        cmd.Parameters.AddWithValue("h", passwordHash);
        cmd.Parameters.AddWithValue("r", role);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
        {
            return null;
        }
        return new User(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3));
    }

    public async Task SetMfaSecretAsync(long userId, string secret, CancellationToken ct = default)
    {
        const string sql = "UPDATE users SET mfa_secret = @s WHERE id = @id;";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("s", secret);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetMfaSecretAsync(long userId, CancellationToken ct = default)
    {
        const string sql = "SELECT mfa_secret FROM users WHERE id = @id;";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", userId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    public async Task EnableMfaAsync(long userId, CancellationToken ct = default)
    {
        const string sql = "UPDATE users SET mfa_enabled = TRUE WHERE id = @id;";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
