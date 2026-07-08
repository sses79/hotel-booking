using HotelBooking.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;

namespace HotelBooking.IntegrationTests.Api;

public sealed class HotelBookingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"HotelBookingApiTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDatabaseProvider>();
            services.RemoveAll<IDbContextOptionsConfiguration<HotelBookingDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<HotelBookingDbContext>>();
            services.RemoveAll<HotelBookingDbContext>();

            services.AddDbContext<HotelBookingDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });
        });
    }
}
