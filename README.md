# Hotel Booking API

Backend developer challenge solution for a hotel room booking API.

The goal is to build a practical ASP.NET Core and EF Core REST API that can:

- Find a hotel by name.
- Find available rooms between two dates for a given number of guests.
- Book a room.
- Find booking details by booking reference.
- Seed predictable test data.
- Reset all data for testing.

The solution intentionally stays small and reviewer-friendly. It is not a hotel
management platform.

## Project Structure

```text
HotelBooking.slnx
src/
  HotelBooking.Api/
  HotelBooking.Models/
  HotelBooking.Repository/
  HotelBooking.Services/
tests/
  HotelBooking.UnitTests/
  HotelBooking.IntegrationTests/
infra/
  local/
docs/
```

## Requirements

- .NET SDK 10
- Docker Desktop
- GitHub CLI for repository/PR work, optional

## Build And Test

```bash
dotnet restore HotelBooking.slnx
dotnet build HotelBooking.slnx --no-restore -m:1 --disable-build-servers
dotnet test HotelBooking.slnx --no-restore --no-build -m:1 --disable-build-servers
```

Run the dependency audit:

```bash
dotnet list HotelBooking.slnx package --vulnerable --include-transitive
```

## Local SQL Server

Local development uses SQL Server in Docker Compose so local EF Core behavior is
close to Azure SQL Database.

Create a local `.env` file:

```bash
cp .env.example .env
```

Start local SQL Server:

```bash
docker compose --env-file .env -f infra/local/compose.yaml up -d sql
```

Validate the Compose file:

```bash
docker compose --env-file .env.example -f infra/local/compose.yaml config --quiet
```

Stop local SQL Server while preserving the database volume:

```bash
docker compose --env-file .env -f infra/local/compose.yaml down
```

Default local connection-string shape:

```text
Server=localhost,1433;Database=HotelBooking;User Id=sa;Password=<MSSQL_SA_PASSWORD>;Encrypt=True;TrustServerCertificate=True
```

## Current API Status

The solution scaffold is in place. Business endpoints are planned but not yet
implemented.

Planned endpoints:

```text
GET  /api/hotels?name=...
GET  /api/hotels/{hotelId}/rooms/available?checkIn=...&checkOut=...&guests=...
POST /api/bookings
GET  /api/bookings/{reference}
POST /api/admin/seed
POST /api/admin/reset
GET  /health
```

## Documentation

- [Challenge Brief](challenge.md)
- [Implementation Plan](docs/plan.md)
- [Solve Challenge Guide](docs/solve-challenge-guide.md)
- [Azure Bicep Guide](docs/Azure-bicep-guide.md)

## CI

GitHub Actions runs:

- restore, build, and test
- whitespace check
- Docker Compose config validation
- NuGet vulnerable dependency audit

CI is read-only and does not deploy to Azure.
