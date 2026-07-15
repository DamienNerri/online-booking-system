using Npgsql;
using OnlineBooking.Api.Common;
using OnlineBooking.Api.Models;

namespace OnlineBooking.Api.Repositories;

/// <summary>
/// Couche d'accès aux données. Toutes les requêtes sont paramétrées (protection
/// contre l'injection SQL, Req 9.2). Le pool de connexions est géré par
/// <see cref="NpgsqlDataSource"/>. Les opérations transactionnelles (réservation,
/// confirmation, annulation) sont ajoutées dans les vagues suivantes.
/// </summary>
public sealed class BookingRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public BookingRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>Recherche les slots disponibles pour un type et une période (Req 1).</summary>
    public async Task<IReadOnlyList<ResourceSlot>> FindAvailableSlotsAsync(
        string resourceType, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, resource_id, resource_type, period_start, period_end, status
            FROM resource_slots
            WHERE resource_type = @type
              AND status = 'AVAILABLE'
              AND period_start >= @from
              AND period_end <= @to
            ORDER BY period_start, resource_id;
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("type", resourceType);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);

        var slots = new List<ResourceSlot>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            slots.Add(MapSlot(reader));
        }
        return slots;
    }

    /// <summary>Retourne une réservation par identifiant, ou null.</summary>
    public async Task<Booking?> GetBookingAsync(long id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, user_id, status, expires_at, created_at
            FROM bookings
            WHERE id = @id;
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }
        return MapBooking(reader);
    }

    public async Task<long[]> GetBookingSlotIdsAsync(long bookingId, CancellationToken ct = default)
    {
        const string sql = "SELECT slot_id FROM booking_slots WHERE booking_id = @b ORDER BY slot_id;";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("b", bookingId);

        var ids = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetInt64(0));
        }
        return ids.ToArray();
    }

    /// <summary>
    /// Crée une réservation atomique (hold) sur un ensemble de slots.
    /// Les lignes visées sont verrouillées via SELECT ... FOR UPDATE, ce qui
    /// sérialise les demandes concurrentes portant sur les mêmes slots
    /// (Req 2, Req 3, Property 1, Property 3). Tout ou rien : la moindre
    /// indisponibilité annule l'intégralité de l'opération (Req 2.2).
    /// </summary>
    public async Task<(long BookingId, DateTime ExpiresAtUtc)> ReserveAsync(
        long userId, long[] slotIds, int holdTtlSeconds, CancellationToken ct = default)
    {
        if (slotIds is null || slotIds.Length == 0)
        {
            throw new ValidationException("Au moins un slot doit être fourni.");
        }

        var expiresAtUtc = DateTime.UtcNow.AddSeconds(holdTtlSeconds);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // 1. Verrouiller les slots visés (sérialisation de la concurrence).
            var statuses = new Dictionary<long, string>();
            await using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = "SELECT id, status FROM resource_slots WHERE id = ANY(@ids) FOR UPDATE";
                sel.Parameters.AddWithValue("ids", slotIds);
                await using var r = await sel.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    statuses[r.GetInt64(0)] = r.GetString(1);
                }
            }

            // 2. Vérifier existence et disponibilité de TOUS les slots.
            foreach (var id in slotIds)
            {
                if (!statuses.TryGetValue(id, out var status))
                {
                    throw new NotFoundException($"Slot {id} introuvable.");
                }
                if (status != "AVAILABLE")
                {
                    throw new ConflictException($"Slot {id} indisponible.");
                }
            }

            // 3. Créer la réservation en HOLD.
            long bookingId;
            await using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO bookings (user_id, status, expires_at) VALUES (@u, 'HOLD', @e) RETURNING id";
                ins.Parameters.AddWithValue("u", userId);
                ins.Parameters.AddWithValue("e", expiresAtUtc);
                bookingId = (long)(await ins.ExecuteScalarAsync(ct))!;
            }

            // 4. Marquer les slots HELD.
            await using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE resource_slots SET status = 'HELD' WHERE id = ANY(@ids)";
                upd.Parameters.AddWithValue("ids", slotIds);
                await upd.ExecuteNonQueryAsync(ct);
            }

            // 5. Lier réservation <-> slots (l'index unique partiel garantit
            //    l'invariant anti-surréservation, même en cas de course résiduelle).
            await using (var link = conn.CreateCommand())
            {
                link.Transaction = tx;
                link.CommandText =
                    "INSERT INTO booking_slots (booking_id, slot_id, active) SELECT @b, s, TRUE FROM unnest(@ids) AS s";
                link.Parameters.AddWithValue("b", bookingId);
                link.Parameters.AddWithValue("ids", slotIds);
                await link.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return (bookingId, expiresAtUtc);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            // Violation de l'index unique partiel = conflit de concurrence.
            await tx.RollbackAsync(ct);
            throw new ConflictException("Un des slots vient d'être réservé par un autre client.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Confirme un hold (HOLD -> CONFIRMED) avant son expiration (Req 4.4).</summary>
    public async Task ConfirmAsync(long bookingId, long userId, bool isAdmin, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var (ownerId, status, expiresAt) = await LockBookingAsync(conn, tx, bookingId, ct);
            EnsureOwner(ownerId, userId, isAdmin);

            if (status != "HOLD")
            {
                throw new ConflictException($"La réservation n'est pas en attente (statut actuel : {status}).");
            }
            if (expiresAt is not null && expiresAt.Value <= DateTime.UtcNow)
            {
                throw new ConflictException("Le hold a expiré.");
            }

            await ExecAsync(conn, tx, "UPDATE bookings SET status = 'CONFIRMED', expires_at = NULL WHERE id = @id",
                ("id", bookingId), ct);
            await ExecAsync(conn, tx,
                "UPDATE resource_slots SET status = 'BOOKED' WHERE id IN (SELECT slot_id FROM booking_slots WHERE booking_id = @id AND active)",
                ("id", bookingId), ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Annule une réservation et libère atomiquement ses slots (Req 5).</summary>
    public async Task CancelAsync(long bookingId, long userId, bool isAdmin, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var (ownerId, status, _) = await LockBookingAsync(conn, tx, bookingId, ct);
            EnsureOwner(ownerId, userId, isAdmin);

            if (status is "CANCELLED" or "EXPIRED")
            {
                throw new ConflictException($"La réservation est déjà {status}.");
            }

            await ExecAsync(conn, tx,
                "UPDATE resource_slots SET status = 'AVAILABLE' WHERE id IN (SELECT slot_id FROM booking_slots WHERE booking_id = @id AND active)",
                ("id", bookingId), ct);
            await ExecAsync(conn, tx, "UPDATE booking_slots SET active = FALSE WHERE booking_id = @id",
                ("id", bookingId), ct);
            await ExecAsync(conn, tx, "UPDATE bookings SET status = 'CANCELLED' WHERE id = @id",
                ("id", bookingId), ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Libère les holds expirés en une seule instruction atomique (Req 4.3).
    /// Retourne le nombre de réservations expirées.
    /// </summary>
    public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken ct = default)
    {
        const string sql = """
            WITH expired AS (
                UPDATE bookings SET status = 'EXPIRED'
                WHERE status = 'HOLD' AND expires_at < now()
                RETURNING id
            ),
            freed_slots AS (
                UPDATE booking_slots SET active = FALSE
                WHERE booking_id IN (SELECT id FROM expired) AND active
                RETURNING slot_id
            ),
            released AS (
                UPDATE resource_slots SET status = 'AVAILABLE'
                WHERE id IN (SELECT slot_id FROM freed_slots)
                RETURNING id
            )
            SELECT (SELECT count(*) FROM expired);
            """;

        await using var cmd = _dataSource.CreateCommand(sql);
        var count = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return (int)count;
    }

    // --- Helpers transactionnels ---

    private static async Task<(long OwnerId, string Status, DateTime? ExpiresAt)> LockBookingAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long bookingId, CancellationToken ct)
    {
        await using var sel = conn.CreateCommand();
        sel.Transaction = tx;
        sel.CommandText = "SELECT user_id, status, expires_at FROM bookings WHERE id = @id FOR UPDATE";
        sel.Parameters.AddWithValue("id", bookingId);
        await using var r = await sel.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
        {
            throw new NotFoundException($"Réservation {bookingId} introuvable.");
        }
        var ownerId = r.GetInt64(0);
        var status = r.GetString(1);
        DateTime? expires = r.IsDBNull(2) ? null : r.GetFieldValue<DateTime>(2);
        return (ownerId, status, expires);
    }

    private static void EnsureOwner(long ownerId, long userId, bool isAdmin)
    {
        if (!isAdmin && ownerId != userId)
        {
            throw new ForbiddenException("Cette réservation ne vous appartient pas.");
        }
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql,
        (string Name, object Value) param, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(param.Name, param.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static ResourceSlot MapSlot(NpgsqlDataReader r) => new(
        Id: r.GetInt64(0),
        ResourceId: r.GetInt64(1),
        ResourceType: r.GetString(2),
        PeriodStart: DateOnly.FromDateTime(r.GetDateTime(3)),
        PeriodEnd: DateOnly.FromDateTime(r.GetDateTime(4)),
        Status: r.GetString(5));

    internal static Booking MapBooking(NpgsqlDataReader r) => new(
        Id: r.GetInt64(0),
        UserId: r.GetInt64(1),
        Status: r.GetString(2),
        ExpiresAt: r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3),
        CreatedAt: r.GetFieldValue<DateTimeOffset>(4));
}
