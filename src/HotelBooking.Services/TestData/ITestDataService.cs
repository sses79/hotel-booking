namespace HotelBooking.Services.TestData;

public interface ITestDataService
{
    Task<SeedTestDataResult> SeedAsync(CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);
}
