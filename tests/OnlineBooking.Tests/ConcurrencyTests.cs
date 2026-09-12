using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using OnlineBooking.Api.Common;
using OnlineBooking.Api.Models;
using OnlineBooking.Api.Repositories;
using Xunit;

namespace OnlineBooking.Tests;

/// <summary>
/// Tests de concurrence validant l'atomicité des réservations (Property 1 &amp; 3 :
/// aucune surréservation malgré des accès simultanés).
///
/// Chaque test lance 2+ opérations en parallèle (Task.WhenAll) sur une base
/// PostgreSQL réelle et vérifie l'absence de race condition.
///
/// Prérequis : une base PostgreSQL de test accessible. Par défaut
/// <c>Host=localhost;Port=5432;Database=booking_test;Username=booking;Password=booking</c>,
/// surchargeable via la variable d'environnement <c>BOOKING_TEST_DB</c>.
///
/// Si aucune base n'est joignable (ex. exécution hors environnement Docker), les
/// tests sont ignorés (Skip) plutôt qu'en échec : la preuve de concurrence se fait
/// stack démarrée. Voir aussi <c>infra/concurrency-test.sh</c>.
/// </summary>
public class ConcurrencyTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("BOOKING_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=booking_test;Username=booking;Password=booking";

    private NpgsqlDataSource _dataSource = null!;
    private BookingRepository _bookingRepo = null!;
    private UserRepository _userRepo = null!;
    private bool _dbAvailable;

    /// <summary>Initialisation : ouvre la connexion, crée le schéma et les repositories.</summary>
    public async Task InitializeAsync()
    {
        _dataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();

        // Vérifier que la base est joignable ; sinon les [Fact] seront ignorés.
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            _dbAvailable = true;
        }
        catch (Exception)
        {
            _dbAvailable = false;
            return;
        }

        await InitializeDatabase();
        await TruncateAll();

        _bookingRepo = new BookingRepository(_dataSource);
        _userRepo = new UserRepository(_dataSource);
    }

    /// <summary>Nettoyage : vide les données puis libère le DataSource.</summary>
    public async Task DisposeAsync()
    {
        if (_dbAvailable)
        {
            try { await TruncateAll(); } catch { /* best effort */ }
        }
        await _dataSource.DisposeAsync();
    }

    private void SkipIfNoDatabase()
    {
        Skip.IfNot(_dbAvailable,
            $"Base PostgreSQL de test injoignable ({ConnectionString}). " +
            "Démarrer la stack (docker compose up -d db) ou définir BOOKING_TEST_DB.");
    }

    /// <summary>Crée le schéma minimal (aligné sur migrations/001_init.sql).</summary>
    private async Task InitializeDatabase()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id BIGSERIAL PRIMARY KEY,
                email TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                role TEXT NOT NULL DEFAULT 'CUSTOMER',
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS resource_slots (
                id BIGSERIAL PRIMARY KEY,
                resource_id BIGINT NOT NULL,
                resource_type TEXT NOT NULL,
                period_start DATE NOT NULL,
                period_end DATE NOT NULL,
                status TEXT NOT NULL DEFAULT 'AVAILABLE',
                CONSTRAINT uq_slot_resource_period
                    UNIQUE (resource_id, period_start, period_end),
                CONSTRAINT ck_slot_period CHECK (period_end > period_start),
                CONSTRAINT ck_slot_status
                    CHECK (status IN ('AVAILABLE', 'HELD', 'BOOKED'))
            );

            CREATE INDEX IF NOT EXISTS ix_slots_search
                ON resource_slots (resource_type, status, period_start, period_end);

            CREATE TABLE IF NOT EXISTS bookings (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES users (id),
                status TEXT NOT NULL,
                expires_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                CONSTRAINT ck_booking_status
                    CHECK (status IN ('HOLD', 'CONFIRMED', 'CANCELLED', 'EXPIRED'))
            );

            CREATE INDEX IF NOT EXISTS ix_bookings_expiry
                ON bookings (status, expires_at);

            CREATE TABLE IF NOT EXISTS booking_slots (
                id BIGSERIAL PRIMARY KEY,
                booking_id BIGINT NOT NULL REFERENCES bookings (id) ON DELETE CASCADE,
                slot_id BIGINT NOT NULL REFERENCES resource_slots (id),
                active BOOLEAN NOT NULL DEFAULT TRUE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS uq_active_slot
                ON booking_slots (slot_id)
                WHERE active = TRUE;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task TruncateAll()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            TRUNCATE TABLE booking_slots RESTART IDENTITY CASCADE;
            TRUNCATE TABLE bookings RESTART IDENTITY CASCADE;
            TRUNCATE TABLE resource_slots RESTART IDENTITY CASCADE;
            TRUNCATE TABLE users RESTART IDENTITY CASCADE;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    // =====================================================================
    // TEST 1 : Deux utilisateurs, même slot → un seul réussit
    // =====================================================================

    [SkippableFact]
    public async Task TwoUsersCannotBookSameSlot_OneSucceedsOneFails()
    {
        SkipIfNoDatabase();

        // ARRANGE : 2 utilisateurs, 1 slot.
        var user1 = await CreateUser("user1@test.com");
        var user2 = await CreateUser("user2@test.com");
        var slotId = await CreateSlot(1, "TEST",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));

        // ACT : deux réservations simultanées sur le même slot.
        var task1 = ReserveSafe(user1.Id, slotId);
        var task2 = ReserveSafe(user2.Id, slotId);
        var outcomes = await Task.WhenAll(task1, task2);

        // ASSERT : exactement un succès et un conflit (409).
        var successes = outcomes.Count(o => o.Success);
        var conflicts = outcomes.Count(o => !o.Success);
        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);

        // Le slot est HELD (une seule fois).
        Assert.Equal("HELD", await GetSlotStatus(slotId));
        Assert.Equal(1, await CountActiveBookingsForSlot(slotId));
    }

    // =====================================================================
    // TEST 2 : Trois utilisateurs, trois slots distincts → tous réussissent
    // =====================================================================

    [SkippableFact]
    public async Task ThreeUsersBookThreeDifferentSlots_AllSucceed()
    {
        SkipIfNoDatabase();

        var users = new List<User>();
        for (int i = 0; i < 3; i++)
        {
            users.Add(await CreateUser($"user{i}@test.com"));
        }

        var slotIds = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            slotIds.Add(await CreateSlot(100 + i, "TEST",
                new DateOnly(2026, 1, 1 + i), new DateOnly(2026, 1, 2 + i)));
        }

        // ACT : chacun réserve son propre slot, en parallèle.
        var tasks = Enumerable.Range(0, 3)
            .Select(i => ReserveSafe(users[i].Id, slotIds[i]))
            .ToList();
        var outcomes = await Task.WhenAll(tasks);

        // ASSERT : tous réussissent.
        Assert.All(outcomes, o => Assert.True(o.Success, o.Error));
        Assert.All(outcomes, o => Assert.True(o.BookingId > 0));
        Assert.Equal(3, await CountAllBookings());
    }

    // =====================================================================
    // TEST 3 : Stress — 50 utilisateurs sur le même slot → un seul réussit
    // =====================================================================

    [SkippableFact]
    public async Task StressTest_50UsersOnSameSlot_OnlyOneSucceeds()
    {
        SkipIfNoDatabase();

        const int n = 50;
        var users = new List<User>();
        for (int i = 0; i < n; i++)
        {
            users.Add(await CreateUser($"stress{i}@test.com"));
        }

        var slotId = await CreateSlot(1, "STRESS",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));

        // ACT : n réservations simultanées sur un unique slot.
        var tasks = users.Select(u => ReserveSafe(u.Id, slotId)).ToList();
        var outcomes = await Task.WhenAll(tasks);

        // ASSERT : exactement 1 succès, n-1 conflits.
        Assert.Equal(1, outcomes.Count(o => o.Success));
        Assert.Equal(n - 1, outcomes.Count(o => !o.Success));
        Assert.Equal("HELD", await GetSlotStatus(slotId));
        Assert.Equal(1, await CountActiveBookingsForSlot(slotId));
    }

    // =====================================================================
    // TEST 4 : L'index unique partiel empêche deux booking_slots actifs
    // =====================================================================

    [SkippableFact]
    public async Task UniqueIndexPreventsDoubleActiveBookingSlots()
    {
        SkipIfNoDatabase();

        var user = await CreateUser("user1@test.com");
        var slotId = await CreateSlot(1, "TEST",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));

        var booking1 = await CreateBookingDirectly(user.Id, "HOLD");
        var booking2 = await CreateBookingDirectly(user.Id, "HOLD");

        await using var conn = await _dataSource.OpenConnectionAsync();

        // Premier lien actif : OK.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO booking_slots (booking_id, slot_id, active) VALUES (@b, @s, TRUE)";
            cmd.Parameters.AddWithValue("b", booking1);
            cmd.Parameters.AddWithValue("s", slotId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Deuxième lien actif sur le même slot : rejeté par l'index unique partiel.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO booking_slots (booking_id, slot_id, active) VALUES (@b, @s, TRUE)";
            cmd.Parameters.AddWithValue("b", booking2);
            cmd.Parameters.AddWithValue("s", slotId);

            var ex = await Assert.ThrowsAsync<PostgresException>(
                async () => await cmd.ExecuteNonQueryAsync());
            Assert.Equal("23505", ex.SqlState); // unique_violation
        }
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    private readonly record struct ReserveOutcome(bool Success, long BookingId, string? Error);

    /// <summary>Tente une réservation et capture le conflit (409) sans lever.</summary>
    private async Task<ReserveOutcome> ReserveSafe(long userId, params long[] slotIds)
    {
        try
        {
            var (bookingId, _) = await _bookingRepo.ReserveAsync(userId, slotIds, 300, default);
            return new ReserveOutcome(true, bookingId, null);
        }
        catch (ConflictException ex)
        {
            return new ReserveOutcome(false, 0, ex.Message);
        }
    }

    private async Task<User> CreateUser(string email)
    {
        var user = await _userRepo.CreateAsync(
            email,
            BCrypt.Net.BCrypt.HashPassword("password123"),
            "CUSTOMER",
            default);
        Assert.NotNull(user);
        return user!;
    }

    private async Task<long> CreateSlot(
        long resourceId, string resourceType, DateOnly periodStart, DateOnly periodEnd)
    {
        await using var cmd = _dataSource.CreateCommand(@"
            INSERT INTO resource_slots (resource_id, resource_type, period_start, period_end)
            VALUES (@r, @t, @s, @e)
            RETURNING id");
        cmd.Parameters.AddWithValue("r", resourceId);
        cmd.Parameters.AddWithValue("t", resourceType);
        cmd.Parameters.AddWithValue("s", periodStart);
        cmd.Parameters.AddWithValue("e", periodEnd);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> GetSlotStatus(long slotId)
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT status FROM resource_slots WHERE id = @id");
        cmd.Parameters.AddWithValue("id", slotId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> CountActiveBookingsForSlot(long slotId)
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT COUNT(*) FROM booking_slots WHERE slot_id = @id AND active = TRUE");
        cmd.Parameters.AddWithValue("id", slotId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> CountAllBookings()
    {
        await using var cmd = _dataSource.CreateCommand("SELECT COUNT(*) FROM bookings");
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CreateBookingDirectly(long userId, string status)
    {
        await using var cmd = _dataSource.CreateCommand(@"
            INSERT INTO bookings (user_id, status, expires_at)
            VALUES (@u, @s, now() + interval '5 minutes')
            RETURNING id");
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("s", status);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
