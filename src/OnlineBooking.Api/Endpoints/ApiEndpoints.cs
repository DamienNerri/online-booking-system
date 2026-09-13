using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OnlineBooking.Api.Common;
using OnlineBooking.Api.Models;
using OnlineBooking.Api.Repositories;
using OnlineBooking.Api.Services;

namespace OnlineBooking.Api.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        MapAuth(app);
        MapMfa(app);
        MapAvailability(app);
        MapBookings(app);
        MapAnalytics(app);
    }

    private static void MapAuth(WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/register", async (RegisterRequest req, AuthService svc, CancellationToken ct) =>
            Results.Ok(await svc.RegisterAsync(req, ct)));

        auth.MapPost("/login", async (LoginRequest req, AuthService svc, CancellationToken ct) =>
            Results.Ok(await svc.LoginAsync(req, ct)));
    }

    private static void MapMfa(WebApplication app)
    {
        var mfa = app.MapGroup("/api/auth/mfa").RequireAuthorization();

        // POST /api/auth/mfa/setup — génère un secret TOTP pour l'utilisateur connecté
        mfa.MapPost("/setup", async (ClaimsPrincipal user, MfaService svc, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var email = user.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? "user";
            var result = await svc.SetupAsync(userId, email, ct);
            return Results.Ok(result);
        });

        // POST /api/auth/mfa/verify — vérifie le code TOTP et active la MFA
        mfa.MapPost("/verify", async (MfaVerifyRequest req, ClaimsPrincipal user,
            MfaService svc, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var ok = await svc.ActivateAsync(userId, req.Code, ct);
            if (!ok) throw new ValidationException("Code TOTP invalide ou expiré.");
            return Results.Ok(new { message = "MFA activée avec succès." });
        });
    }

    private static void MapAvailability(WebApplication app)
    {
        // Consultation ouverte, sans authentification (Req 1).
        app.MapGet("/api/availability", async (
            [FromQuery] string type,
            [FromQuery] string from,
            [FromQuery] string to,
            BookingService svc,
            CancellationToken ct) =>
        {
            if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
            {
                throw new ValidationException("Paramètres 'from'/'to' invalides (format attendu : yyyy-MM-dd).");
            }
            var items = await svc.GetAvailabilityAsync(type, fromDate, toDate, ct);
            return Results.Ok(items);
        });
    }

    private static void MapBookings(WebApplication app)
    {
        // Authentification requise (Req 8).
        var bookings = app.MapGroup("/api/bookings").RequireAuthorization();

        bookings.MapGet("/", async (ClaimsPrincipal user,
            BookingService svc, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var userBookings = await svc.GetUserBookingsAsync(userId, ct);
            return Results.Ok(userBookings);
        });

        bookings.MapPost("/", async (CreateBookingRequest req, ClaimsPrincipal user,
            BookingService svc, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var result = await svc.CreateAsync(userId, req, ct);
            return Results.Created($"/api/bookings/{result.BookingId}", result);
        });

        bookings.MapPost("/{id:long}/confirm", async (long id, ClaimsPrincipal user,
            BookingService svc, CancellationToken ct) =>
        {
            var result = await svc.ConfirmAsync(id, GetUserId(user), IsAdmin(user), ct);
            return Results.Ok(result);
        });

        bookings.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user,
            BookingService svc, CancellationToken ct) =>
        {
            await svc.CancelAsync(id, GetUserId(user), IsAdmin(user), ct);
            return Results.NoContent();
        });
    }

    private static void MapAnalytics(WebApplication app)
    {
        // Accès réservé aux ADMIN — données agrégées anonymisées (Contrainte C6)
        var analytics = app.MapGroup("/api/admin/analytics").RequireAuthorization();

        analytics.MapGet("/", async (ClaimsPrincipal user, AnalyticsRepository repo, CancellationToken ct) =>
        {
            if (!IsAdmin(user))
                throw new ForbiddenException("Accès réservé aux administrateurs.");

            var summary = await repo.GetSummaryAsync(ct);
            var topRooms = await repo.GetTopRoomsAsync(10, ct);
            var weeklyTrend = await repo.GetWeeklyTrendAsync(12, ct);

            return Results.Ok(new { summary, topRooms, weeklyTrend });
        });
    }

    // --- Helpers ---

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static long GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null || !long.TryParse(sub, out var id))
        {
            throw new UnauthorizedException();
        }
        return id;
    }

    private static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole("ADMIN");
}
