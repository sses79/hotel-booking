using HotelBooking.Repository.Data;
using HotelBooking.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace HotelBooking.IntegrationTests.Api;

public sealed class HotelBookingApiFactory(
    SqlServerFixture sqlServer,
    DbCommandInterceptor? commandInterceptor = null)
    : WebApplicationFactory<Program>
{
    private readonly string _connectionString = sqlServer.CreateConnectionString();

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
                options.UseSqlServer(
                    _connectionString,
                    sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(2),
                        errorNumbersToAdd: null));

                if (commandInterceptor is not null)
                {
                    options.AddInterceptors(commandInterceptor);
                }
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HotelBookingDbContext>();
        dbContext.Database.Migrate();

        return host;
    }
}
