namespace OnlineBooking.Api.Configuration;

/// <summary>Options de sécurité JWT.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "online-booking-system";
    public int ExpiresMinutes { get; set; } = 60;
}

/// <summary>Options métier de réservation.</summary>
public sealed class BookingOptions
{
    public const string SectionName = "Booking";
    public int HoldTtlSeconds { get; set; } = 300;
}

/// <summary>Options de limitation de débit.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";
    public int WindowSeconds { get; set; } = 60;
    public int MaxRequests { get; set; } = 100;
}
