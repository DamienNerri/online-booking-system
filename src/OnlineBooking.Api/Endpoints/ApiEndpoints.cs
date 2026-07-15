using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OnlineBooking.Api.Common;
using OnlineBooking.Api.Models;
using OnlineBooking.Api.Services;

namespace OnlineBooking.Api.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        MapAuth(app);
        MapAvailability(app);
        MapBookings(app);
    }

    private static void MapAuth(WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/register", async (RegisterRequest req, AuthService svc, CancellationToken ct) =>
            Results.Ok(await svc.RegisterAsync(req, ct)));

        auth.MapPost("/login", async (LoginRequest req, AuthService svc, CancellationToken ct) =>
            Results.Ok(await svc.LoginAsync(req, ct)));
    }

    private static void MapAvailability(WebApplication app)
    {
        // Consultation ouverte (Req 1) : pas d'authentification requise pour chercher.
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
        // Toutes les opérations de réservation exigent une authentification (Req 8).
        var bookings = app.MapGroup("/api/bookings").RequireAuthorization();

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
