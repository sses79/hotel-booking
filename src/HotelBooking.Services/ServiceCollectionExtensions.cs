using HotelBooking.Services.TestData;
using Microsoft.Extensions.DependencyInjection;

namespace HotelBooking.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHotelBookingServices(this IServiceCollection services)
    {
        services.AddScoped<ITestDataService, TestDataService>();

        return services;
    }
}
