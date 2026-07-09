using HotelBooking.Models;
using HotelBooking.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.Services.Rooms;

public sealed class RoomAvailabilityService(
    HotelBookingDbContext dbContext,
    TimeProvider timeProvider)
    : IRoomAvailabilityService
{
    public async Task<IReadOnlyList<AvailableRoomResult>> FindAvailableRoomsAsync(
        Guid hotelId,
        DateOnly checkInDate,
        DateOnly checkOutDate,
        int guests,
        RoomType? roomType = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (!BookingRules.HasFutureCheckInDate(checkInDate, today)
            || !BookingRules.HasValidDateRange(checkInDate, checkOutDate)
            || !BookingRules.HasValidGuestCount(guests))
        {
            return [];
        }

        var query = dbContext.Rooms
            .AsNoTracking()
            .Where(room => room.HotelId == hotelId)
            .Where(room => room.Capacity >= guests);

        if (roomType is not null)
        {
            query = query.Where(room => room.RoomType == roomType.Value);
        }

        var rooms = await query
            .Where(room => !room.Bookings.Any(booking =>
                booking.CheckInDate < checkOutDate
                && checkInDate < booking.CheckOutDate))
            .ToListAsync(cancellationToken);

        return BookingRules.OrderRoomsForBooking(rooms)
            .Select(room => new AvailableRoomResult(
                room.Id,
                room.HotelId,
                room.RoomNumber,
                room.RoomType,
                room.Capacity))
            .ToList();
    }
}
