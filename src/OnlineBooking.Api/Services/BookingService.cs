using Microsoft.Extensions.Options;
using OnlineBooking.Api.Common;
using OnlineBooking.Api.Configuration;
using OnlineBooking.Api.Models;
using OnlineBooking.Api.Repositories;

namespace OnlineBooking.Api.Services;

/// <summary>
/// Logique métier de réservation. Délègue l'atomicité et la sérialisation de la
/// concurrence au repository (transactions PostgreSQL + FOR UPDATE).
/// </summary>
public sealed class BookingService
{
    private readonly BookingRepository _repo;
    private readonly BookingOptions _options;
    private readonly ILogger<BookingService> _logger;

    public BookingService(BookingRepository repo, IOptions<BookingOptions> options, ILogger<BookingService> logger)
    {
        _repo = repo;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AvailabilityItem>> GetAvailabilityAsync(
        string resourceType, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            throw new ValidationException("Veuillez sélectionner un type de chambre.");
        }
        if (to <= from)
        {
            throw new ValidationException("La date de départ doit être postérieure à la date d'arrivée.");
        }

        // 🔴 FIX 1 : Validation dates passées
        if (from < DateOnly.FromDateTime(DateTime.Today))
        {
            throw new ValidationException("Impossible de réserver dans le passé. Veuillez choisir une date d'arrivée future.");
        }

        var slots = await _repo.FindAvailableSlotsAsync(resourceType, from, to, ct);
        return slots
            .Select(s => new AvailabilityItem(s.Id, s.ResourceId, s.ResourceType, s.PeriodStart, s.PeriodEnd))
            .ToList();
    }

    public async Task<BookingResponse> CreateAsync(long userId, CreateBookingRequest req, CancellationToken ct = default)
    {
        if (req.SlotIds is null || req.SlotIds.Length == 0)
        {
            throw new ValidationException("Veuillez sélectionner au moins une chambre.");
        }
        if (req.SlotIds.Distinct().Count() != req.SlotIds.Length)
        {
            throw new ValidationException("Une même chambre a été sélectionnée plusieurs fois.");
        }

        var (bookingId, expiresAtUtc) = await _repo.ReserveAsync(userId, req.SlotIds, _options.HoldTtlSeconds, ct);
        _logger.LogInformation("Réservation {BookingId} créée (HOLD) par utilisateur {UserId} sur {Count} slot(s)",
            bookingId, userId, req.SlotIds.Length);

        return new BookingResponse(bookingId, "HOLD",
            new DateTimeOffset(expiresAtUtc, TimeSpan.Zero), req.SlotIds.OrderBy(x => x).ToArray());
    }

    public async Task<BookingResponse> ConfirmAsync(long bookingId, long userId, bool isAdmin, CancellationToken ct = default)
    {
        await _repo.ConfirmAsync(bookingId, userId, isAdmin, ct);
        _logger.LogInformation("Réservation {BookingId} confirmée par utilisateur {UserId}", bookingId, userId);
        var slotIds = await _repo.GetBookingSlotIdsAsync(bookingId, ct);
        return new BookingResponse(bookingId, "CONFIRMED", null, slotIds);
    }

    public async Task CancelAsync(long bookingId, long userId, bool isAdmin, CancellationToken ct = default)
    {
        await _repo.CancelAsync(bookingId, userId, isAdmin, ct);
        _logger.LogInformation("Réservation {BookingId} annulée par utilisateur {UserId}", bookingId, userId);
    }

    // 🔴 FIX 3 : Récupérer toutes les réservations utilisateur
    public async Task<IReadOnlyList<BookingResponse>> GetUserBookingsAsync(
        long userId, CancellationToken ct = default)
    {
        var bookings = await _repo.GetUserBookingsAsync(userId, ct);
        return bookings;
    }
}
