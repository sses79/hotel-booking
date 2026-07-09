# Hotel Booking Challenge Solution

## Summary

This solution implements the requested hotel room booking API using ASP.NET
Core, EF Core, and SQL Server. It focuses on the challenge's core value:
correct booking rules, a clear project breakdown, straightforward testing, and
a small repeatable Azure deployment.

Live API:
[Swagger UI](https://ca-hotel-booking-dev-qjygtiyk.agreeableriver-d928ad99.uksouth.azurecontainerapps.io/swagger)

Source code:
[GitHub repository](https://github.com/sses79/hotel-booking)

## Architecture

```text
HotelBooking.Api
  -> HotelBooking.Services
  -> HotelBooking.Repository
  -> HotelBooking.Models
  -> SQL Server / Azure SQL
```

- `HotelBooking.Api` owns REST controllers, DTOs, Swagger, validation responses,
  health checks, and dependency registration.
- `HotelBooking.Services` owns hotel search, availability, booking, booking
  lookup, booking rules, and seed/reset behavior.
- `HotelBooking.Repository` owns the EF Core `DbContext` and relational model
  configuration.
- `HotelBooking.Models` owns `Hotel`, `Room`, `Booking`, and `RoomType`.
- Unit tests cover pure rules; Testcontainers-backed SQL Server integration
  tests cover migrations, API workflows, and persistence behavior.

This separation is intentional but restrained: enough structure to explain the
solution without adding layers that the challenge does not need.

## Requirement Mapping

| Requirement | Solution |
| --- | --- |
| Find a hotel by name | `GET /api/hotels?name=...` |
| Find available rooms | `GET /api/hotels/{hotelId}/rooms/available` |
| Book one room for a stay | `POST /api/bookings` |
| Find booking by reference | `GET /api/bookings/{reference}` |
| Seed test data | `POST /api/admin/seed` |
| Reset test data | `POST /api/admin/reset` |
| OpenAPI testing | Swagger UI and OpenAPI JSON |
| No authentication | Intentionally omitted as requested |
| Optional Azure hosting | Container Apps and Azure SQL serverless |

## Core Booking Rules

- The seeded hotel has six rooms: two single, two double, and two deluxe.
- Capacity is checked before a room can be selected.
- A booking keeps the same room for the entire stay.
- Dates use `DateOnly` and half-open ranges: `[checkIn, checkOut)`.
- Back-to-back stays are allowed because checkout day is not occupied.
- Overlap uses:

```text
existing.CheckInDate < requested.CheckOutDate
&& requested.CheckInDate < existing.CheckOutDate
```

- Check-in must be later than the current UTC date.
- Checkout must be later than check-in.
- Available rooms are ordered deterministically by capacity, room type, and
  room number.
- Booking references have a unique database index.
- Booking creation checks availability inside a transaction before saving.

Invalid requests use ASP.NET Core `ProblemDetails`.

## Test Data

The seed endpoint creates one predictable hotel:

```text
Grand Plaza Hotel
ID: 00000000-0000-0000-0000-000000000001

101, 102  Single  capacity 1
201, 202  Double  capacity 2
301, 302  Deluxe  capacity 4
```

Seeding resets existing data first, making reviewer tests repeatable. The reset
endpoint removes bookings, rooms, and hotels.

## Running And Testing

```bash
dotnet restore HotelBooking.slnx
dotnet build HotelBooking.slnx --no-restore
dotnet test HotelBooking.slnx --no-build --no-restore
```

Run SQL Server and the API together:

```bash
cp .env.example .env
docker compose --env-file .env -f infra/local/compose.yaml up --build -d
```

Local Swagger is available at `http://localhost:5080/swagger`.

Automated coverage includes date validation, overlap, back-to-back bookings,
capacity, deterministic room selection, availability, booking creation and
lookup, seed/reset, Swagger, and OpenAPI.

## Azure Deployment

```text
Public immutable GHCR image
  -> Azure Container Apps Consumption (0-2 replicas)
  -> Azure SQL Database serverless (60-minute auto-pause)
```

Bicep provisions the environment. GitHub Actions builds SHA-tagged API images,
and `scripts/check-azure-resources.sh` verifies the deployed image, scaling,
SQL configuration, absence of paid monitoring resources, and API health.

Application Insights, Log Analytics, Azure Container Registry, authentication,
and a frontend are deliberately outside this challenge's scope.

## Main Trade-Off

The transaction and in-transaction availability check are appropriate for this
challenge. A high-traffic production system should add stronger SQL-specific
concurrency control to prevent two simultaneous requests from selecting the
same final room. This is documented rather than hidden behind unnecessary
complexity.

See [README](../README.md), [implementation plan](plan.md), and
[Azure deployment runbook](Azure-deployment.md) for detailed commands. See
[booking concurrency future improvement](booking-concurrency-future-improvement.md)
for the production-hardening design.
