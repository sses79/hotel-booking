using HotelBooking.Models;

namespace HotelBooking.Services.TestData;

public static class SeedData
{
    public static readonly Guid GrandPlazaHotelId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public const string GrandPlazaHotelName = "Grand Plaza Hotel";

    public static IReadOnlyList<SeedRoomDefinition> Rooms { get; } =
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000101"), "101", RoomType.Single, 1),
        new(Guid.Parse("00000000-0000-0000-0000-000000000102"), "102", RoomType.Single, 1),
        new(Guid.Parse("00000000-0000-0000-0000-000000000201"), "201", RoomType.Double, 2),
        new(Guid.Parse("00000000-0000-0000-0000-000000000202"), "202", RoomType.Double, 2),
        new(Guid.Parse("00000000-0000-0000-0000-000000000301"), "301", RoomType.Deluxe, 4),
        new(Guid.Parse("00000000-0000-0000-0000-000000000302"), "302", RoomType.Deluxe, 4)
    ];

    public static Hotel CreateGrandPlazaHotel()
    {
        return new Hotel
        {
            Id = GrandPlazaHotelId,
            Name = GrandPlazaHotelName,
            Rooms = Rooms
                .Select(room => new Room
                {
                    Id = room.Id,
                    HotelId = GrandPlazaHotelId,
                    RoomNumber = room.RoomNumber,
                    RoomType = room.RoomType,
                    Capacity = room.Capacity
                })
                .ToList()
        };
    }
}
