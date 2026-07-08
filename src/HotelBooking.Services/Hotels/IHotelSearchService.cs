namespace HotelBooking.Services.Hotels;

public interface IHotelSearchService
{
    Task<IReadOnlyList<HotelSearchResult>> SearchAsync(
        string? name,
        CancellationToken cancellationToken = default);
}
