using HotelBooking.IntegrationTests.Infrastructure;
using HotelBooking.Models;
using HotelBooking.Repository.Data;
using HotelBooking.Services.TestData;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class TestDataServiceTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Seed_creates_expected_hotel_and_six_rooms()
    {
        await using var database = await CreateDatabaseAsync();
        var service = new TestDataService(database.Context);

        var result = await service.SeedAsync();

        var hotel = await database.Context.Hotels
            .Include(hotel => hotel.Rooms)
            .SingleAsync();

        Assert.Equal(SeedData.GrandPlazaHotelId, result.HotelId);
        Assert.Equal(SeedData.GrandPlazaHotelName, result.HotelName);
        Assert.Equal(6, result.RoomsCreated);
        Assert.Equal(SeedData.GrandPlazaHotelName, hotel.Name);
        Assert.Equal(6, hotel.Rooms.Count);
        Assert.Contains(hotel.Rooms, room =>
            room.RoomNumber == "101"
            && room.RoomType == RoomType.Single
            && room.Capacity == 1);
        Assert.Contains(hotel.Rooms, room =>
            room.RoomNumber == "302"
            && room.RoomType == RoomType.Deluxe
            && room.Capacity == 4);
    }

    [Fact]
    public async Task Seed_resets_existing_data_before_reseeding()
    {
        await using var database = await CreateDatabaseAsync();
        var service = new TestDataService(database.Context);

        await service.SeedAsync();
        database.Context.Bookings.Add(new Booking
        {
            BookingReference = "HB-TEST-001",
            HotelId = SeedData.GrandPlazaHotelId,
            RoomId = SeedData.Rooms[0].Id,
            GuestName = "Ada Lovelace",
            GuestCount = 1,
            CheckInDate = new DateOnly(2026, 8, 1),
            CheckOutDate = new DateOnly(2026, 8, 3)
        });
        await database.Context.SaveChangesAsync();

        await service.SeedAsync();

        Assert.Equal(1, await database.Context.Hotels.CountAsync());
        Assert.Equal(6, await database.Context.Rooms.CountAsync());
        Assert.Equal(0, await database.Context.Bookings.CountAsync());
    }

    [Fact]
    public async Task Reset_removes_all_data()
    {
        await using var database = await CreateDatabaseAsync();
        var service = new TestDataService(database.Context);

        await service.SeedAsync();

        await service.ResetAsync();

        Assert.Equal(0, await database.Context.Bookings.CountAsync());
        Assert.Equal(0, await database.Context.Rooms.CountAsync());
        Assert.Equal(0, await database.Context.Hotels.CountAsync());
    }

    private async Task<TestDatabase> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<HotelBookingDbContext>()
            .UseSqlServer(sqlServer.CreateConnectionString())
            .Options;

        var context = new HotelBookingDbContext(options);
        await context.Database.MigrateAsync();

        return new TestDatabase(context);
    }

    private sealed class TestDatabase(HotelBookingDbContext context)
        : IAsyncDisposable
    {
        public HotelBookingDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }
}
