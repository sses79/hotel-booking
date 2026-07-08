using HotelBooking.Models;

namespace HotelBooking.Services.TestData;

public sealed record SeedRoomDefinition(
    Guid Id,
    string RoomNumber,
    RoomType RoomType,
    int Capacity);
