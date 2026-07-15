namespace OnlineBooking.Api.Models;

// --- Authentification ---
public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string Token, string Email, string Role);

// --- Réservation ---
public sealed record CreateBookingRequest(long[] SlotIds);
public sealed record BookingResponse(long BookingId, string Status, DateTimeOffset? ExpiresAt, long[] SlotIds);

// --- Disponibilité ---
public sealed record AvailabilityItem(long SlotId, long ResourceId, string ResourceType,
    DateOnly PeriodStart, DateOnly PeriodEnd);
