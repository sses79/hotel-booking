using HotelBooking.Services.Bookings;
using HotelBooking.Services.Hotels;
using HotelBooking.Services.Rooms;
using HotelBooking.Services.TestData;
using Microsoft.Extensions.DependencyInjection;

namespace HotelBooking.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHotelBookingServices(this IServiceCollection services)
    {
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IHotelSearchService, HotelSearchService>();
        services.AddScoped<IRoomAvailabilityService, RoomAvailabilityService>();
        services.AddScoped<ITestDataService, TestDataService>();

        return services;
    }
}
