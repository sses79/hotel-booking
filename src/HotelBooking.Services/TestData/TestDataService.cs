using HotelBooking.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.Services.TestData;

public sealed class TestDataService(HotelBookingDbContext dbContext) : ITestDataService
{
    public async Task<SeedTestDataResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        await ResetAsync(cancellationToken);

        var hotel = SeedData.CreateGrandPlazaHotel();

        dbContext.Hotels.Add(hotel);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SeedTestDataResult(
            hotel.Id,
            hotel.Name,
            hotel.Rooms.Count);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        var bookings = await dbContext.Bookings.ToListAsync(cancellationToken);
        dbContext.Bookings.RemoveRange(bookings);

        var rooms = await dbContext.Rooms.ToListAsync(cancellationToken);
        dbContext.Rooms.RemoveRange(rooms);

        var hotels = await dbContext.Hotels.ToListAsync(cancellationToken);
        dbContext.Hotels.RemoveRange(hotels);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
