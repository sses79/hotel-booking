using HotelBooking.Models;

namespace HotelBooking.Services.Rooms;

public sealed record AvailableRoomResult(
    Guid Id,
    Guid HotelId,
    string RoomNumber,
    RoomType RoomType,
    int Capacity);
