using HotelBooking.Models;
using HotelBooking.Services;

namespace HotelBooking.UnitTests;

public sealed class BookingRulesTests
{
    [Fact]
    public void Check_in_date_cannot_be_before_today()
    {
        var today = new DateOnly(2026, 7, 9);

        Assert.True(BookingRules.HasNonPastCheckInDate(today.AddDays(1), today));
        Assert.True(BookingRules.HasNonPastCheckInDate(today, today));
        Assert.False(BookingRules.HasNonPastCheckInDate(today.AddDays(-1), today));
    }

    [Fact]
    public void Check_out_date_must_be_after_check_in_date()
    {
        var checkIn = new DateOnly(2026, 8, 1);

        Assert.True(BookingRules.HasValidDateRange(checkIn, checkIn.AddDays(1)));
        Assert.False(BookingRules.HasValidDateRange(checkIn, checkIn));
        Assert.False(BookingRules.HasValidDateRange(checkIn, checkIn.AddDays(-1)));
    }

    [Fact]
    public void Overlap_returns_true_when_date_ranges_share_a_night()
    {
        var overlaps = BookingRules.DateRangesOverlap(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 2),
            new DateOnly(2026, 8, 4));

        Assert.True(overlaps);
    }

    [Fact]
    public void Overlap_returns_false_for_back_to_back_bookings()
    {
        var overlaps = BookingRules.DateRangesOverlap(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 5));

        Assert.False(overlaps);
    }

    [Fact]
    public void Room_capacity_must_cover_guest_count()
    {
        var room = new Room
        {
            HotelId = Guid.NewGuid(),
            RoomNumber = "201",
            RoomType = RoomType.Double,
            Capacity = 2
        };

        Assert.True(BookingRules.CanRoomHoldGuests(room, 2));
        Assert.False(BookingRules.CanRoomHoldGuests(room, 3));
        Assert.False(BookingRules.CanRoomHoldGuests(room, 0));
    }

    [Fact]
    public void Room_order_prefers_smallest_suitable_room_then_stable_room_number()
    {
        var rooms = new[]
        {
            CreateRoom("302", RoomType.Deluxe, 4),
            CreateRoom("201", RoomType.Double, 2),
            CreateRoom("202", RoomType.Double, 2),
            CreateRoom("101", RoomType.Single, 1)
        };

        var orderedRoomNumbers = BookingRules.OrderRoomsForBooking(rooms)
            .Select(room => room.RoomNumber);

        Assert.Equal(["101", "201", "202", "302"], orderedRoomNumbers);
    }

    private static Room CreateRoom(string roomNumber, RoomType roomType, int capacity)
    {
        return new Room
        {
            HotelId = Guid.NewGuid(),
            RoomNumber = roomNumber,
            RoomType = roomType,
            Capacity = capacity
        };
    }
}
