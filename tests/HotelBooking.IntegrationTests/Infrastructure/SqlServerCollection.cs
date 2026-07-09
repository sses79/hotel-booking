using Testcontainers.MsSql;

namespace HotelBooking.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public Task InitializeAsync()
    {
        return _container.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }

    public string CreateConnectionString()
    {
        var connectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            _container.GetConnectionString())
        {
            InitialCatalog = $"HotelBookingTests_{Guid.NewGuid():N}"
        };

        return connectionString.ConnectionString;
    }
}
