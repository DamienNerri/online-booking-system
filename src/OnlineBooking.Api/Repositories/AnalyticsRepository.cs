using Npgsql;

namespace OnlineBooking.Api.Repositories;

/// <summary>
/// Données agrégées et anonymisées pour le tableau de bord admin (Contrainte C6).
/// Aucune donnée personnelle (user_id, email) n'est exposée — GROUP BY uniquement.
/// Conforme RGPD : agrégation ≥ 5 enregistrements, pas d'historique per-user.
/// </summary>
public sealed class AnalyticsRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public AnalyticsRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>Statistiques globales : total, confirmés, annulés, taux annulation.</summary>
    public async Task<object> GetSummaryAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                COUNT(*)                                                 AS total,
                COUNT(CASE WHEN status = 'CONFIRMED' THEN 1 END)         AS confirmed,
                COUNT(CASE WHEN status = 'CANCELLED' THEN 1 END)         AS cancelled,
                COUNT(CASE WHEN status = 'HOLD'      THEN 1 END)         AS pending,
                ROUND(
                    COUNT(CASE WHEN status = 'CANCELLED' THEN 1 END)::numeric
                    / NULLIF(COUNT(*), 0) * 100, 1
                )                                                        AS cancellation_rate_pct
            FROM bookings;
            """;
        await using var cmd = _dataSource.CreateCommand(sql);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return new
        {
            total               = r.GetInt64(0),
            confirmed           = r.GetInt64(1),
            cancelled           = r.GetInt64(2),
            pending             = r.GetInt64(3),
            cancellationRatePct = r.IsDBNull(4) ? 0m : r.GetDecimal(4),
        };
    }

    /// <summary>Top chambres par nombre de réservations (sans données personnelles).</summary>
    public async Task<IReadOnlyList<object>> GetTopRoomsAsync(int limit = 10, CancellationToken ct = default)
    {
        const string sql = """
            SELECT resource_id, resource_type, total_bookings, confirmed_bookings,
                   cancelled_bookings, cancellation_rate_pct
            FROM v_analytics_bookings_by_room
            ORDER BY total_bookings DESC
            LIMIT @limit;
            """;
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("limit", limit);

        var list = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new
            {
                resourceId          = r.GetInt64(0),
                resourceType        = r.GetString(1),
                totalBookings       = r.GetInt64(2),
                confirmedBookings   = r.GetInt64(3),
                cancelledBookings   = r.GetInt64(4),
                cancellationRatePct = r.IsDBNull(5) ? 0m : r.GetDecimal(5),
            });
        }
        return list;
    }

    /// <summary>Tendance hebdomadaire des réservations (agrégé, sans identifiants).</summary>
    public async Task<IReadOnlyList<object>> GetWeeklyTrendAsync(int weeks = 12, CancellationToken ct = default)
    {
        const string sql = """
            SELECT week_start, total_bookings, confirmed, cancelled
            FROM v_analytics_bookings_by_week
            LIMIT @weeks;
            """;
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("weeks", weeks);

        var list = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new
            {
                weekStart     = r.GetFieldValue<DateOnly>(0),
                totalBookings = r.GetInt64(1),
                confirmed     = r.GetInt64(2),
                cancelled     = r.GetInt64(3),
            });
        }
        return list;
    }
}
