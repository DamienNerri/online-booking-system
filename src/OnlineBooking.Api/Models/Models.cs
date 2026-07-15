namespace OnlineBooking.Api.Models;

public enum SlotStatus { AVAILABLE, HELD, BOOKED }

public enum BookingStatus { HOLD, CONFIRMED, CANCELLED, EXPIRED }

/// <summary>Unité réservable atomique : une ressource sur une période donnée.</summary>
public sealed record ResourceSlot(
    long Id,
    long ResourceId,
    string ResourceType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status);

/// <summary>Engagement d'un utilisateur sur un ou plusieurs slots.</summary>
public sealed record Booking(
    long Id,
    long UserId,
    string Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record User(
    long Id,
    string Email,
    string PasswordHash,
    string Role);
