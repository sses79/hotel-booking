using HotelBooking.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HotelBooking.Repository;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "HotelBooking";

    public static IServiceCollection AddHotelBookingRepository(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing connection string: ConnectionStrings:{ConnectionStringName}");
        }

        services.AddDbContext<HotelBookingDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services;
    }
}
