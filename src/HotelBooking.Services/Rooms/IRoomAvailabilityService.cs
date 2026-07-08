using HotelBooking.Models;

namespace HotelBooking.Services.Rooms;

public interface IRoomAvailabilityService
{
    Task<IReadOnlyList<AvailableRoomResult>> FindAvailableRoomsAsync(
        Guid hotelId,
        DateOnly checkInDate,
        DateOnly checkOutDate,
        int guests,
        RoomType? roomType = null,
        CancellationToken cancellationToken = default);
}
