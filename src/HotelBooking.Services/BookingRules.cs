using HotelBooking.Models;

namespace HotelBooking.Services;

public static class BookingRules
{
    public static bool HasNonPastCheckInDate(DateOnly checkInDate, DateOnly today)
    {
        return checkInDate >= today;
    }

    public static bool HasValidDateRange(DateOnly checkInDate, DateOnly checkOutDate)
    {
        return checkInDate < checkOutDate;
    }

    public static bool HasValidGuestCount(int guestCount)
    {
        return guestCount >= 1;
    }

    public static bool CanRoomHoldGuests(Room room, int guestCount)
    {
        return HasValidGuestCount(guestCount) && room.Capacity >= guestCount;
    }

    public static bool DateRangesOverlap(
        DateOnly existingCheckInDate,
        DateOnly existingCheckOutDate,
        DateOnly requestedCheckInDate,
        DateOnly requestedCheckOutDate)
    {
        return existingCheckInDate < requestedCheckOutDate
            && requestedCheckInDate < existingCheckOutDate;
    }

    public static IOrderedEnumerable<Room> OrderRoomsForBooking(IEnumerable<Room> rooms)
    {
        return rooms
            .OrderBy(room => room.Capacity)
            .ThenBy(room => GetRoomTypeOrder(room.RoomType))
            .ThenBy(room => room.RoomNumber, StringComparer.OrdinalIgnoreCase);
    }

    public static int GetRoomTypeOrder(RoomType roomType)
    {
        return roomType switch
        {
            RoomType.Single => 1,
            RoomType.Double => 2,
            RoomType.Deluxe => 3,
            _ => int.MaxValue
        };
    }
}
