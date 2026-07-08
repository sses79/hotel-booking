using HotelBooking.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.Services.Hotels;

public sealed class HotelSearchService(HotelBookingDbContext dbContext) : IHotelSearchService
{
    public async Task<IReadOnlyList<HotelSearchResult>> SearchAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Hotels.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var normalizedName = name.Trim();

            query = query.Where(hotel => hotel.Name.Contains(normalizedName));
        }

        return await query
            .OrderBy(hotel => hotel.Name)
            .Select(hotel => new HotelSearchResult(hotel.Id, hotel.Name))
            .ToListAsync(cancellationToken);
    }
}
