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
            throw new ValidationException("Le type de ressource est requis.");
        }
        if (to <= from)
        {
            throw new ValidationException("La date de fin doit être postérieure à la date de début.");
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
            throw new ValidationException("Au moins un slot doit être fourni.");
        }
        if (req.SlotIds.Distinct().Count() != req.SlotIds.Length)
        {
            throw new ValidationException("Les slots ne doivent pas être dupliqués.");
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
}
