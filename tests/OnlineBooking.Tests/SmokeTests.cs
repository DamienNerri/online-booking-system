using OnlineBooking.Api.Models;
using Xunit;

namespace OnlineBooking.Tests;

/// <summary>
/// Tests de fumée de la vague 1 : valident que le socle (modèles, build) est
/// cohérent. Les tests de concurrence et d'intégration arrivent aux vagues 3 et 4.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void ResourceSlot_ExposesItsFields()
    {
        var slot = new ResourceSlot(1, 42, "HOTEL_ROOM",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), "AVAILABLE");

        Assert.Equal(42, slot.ResourceId);
        Assert.Equal("AVAILABLE", slot.Status);
        Assert.True(slot.PeriodEnd > slot.PeriodStart);
    }

    [Fact]
    public void BookingStatus_HasExpectedStates()
    {
        Assert.Equal(4, Enum.GetValues<BookingStatus>().Length);
        Assert.Contains(BookingStatus.HOLD, Enum.GetValues<BookingStatus>());
    }
}
