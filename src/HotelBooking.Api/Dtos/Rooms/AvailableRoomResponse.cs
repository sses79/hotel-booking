using HotelBooking.Models;

namespace HotelBooking.Api.Dtos.Rooms;

public sealed record AvailableRoomResponse(
    Guid Id,
    Guid HotelId,
    string RoomNumber,
    RoomType RoomType,
    int Capacity);
