# Large-Project Architecture Evolution

## Purpose

This document describes how the current hotel booking solution could evolve if
it became a larger, long-lived .NET application maintained by multiple teams.

It is not the recommended structure for the current backend challenge. The
existing `Models`, `Repository`, and `Services` projects are intentionally
smaller and easier for a reviewer to understand. The additional boundaries
below become useful only when application size, business complexity, testing
needs, or team ownership justify them.

## Suggested Project Structure

```text
src/
  HotelBooking.Api/
  HotelBooking.Application/
  HotelBooking.Domain/
  HotelBooking.Infrastructure/
tests/
  HotelBooking.Domain.UnitTests/
  HotelBooking.Application.UnitTests/
  HotelBooking.IntegrationTests/
  HotelBooking.ArchitectureTests/
```

The current projects would broadly evolve as follows:

| Current project | Larger architecture |
|---|---|
| `HotelBooking.Api` | `HotelBooking.Api` |
| `HotelBooking.Models` | `HotelBooking.Domain` |
| `HotelBooking.Services` | Mostly `HotelBooking.Application` |
| `HotelBooking.Repository` | `HotelBooking.Infrastructure` |

This should not be a mechanical rename. Classes should move according to their
responsibilities and dependency requirements.

## Dependency Direction

```text
HotelBooking.Api
    -> HotelBooking.Application
    -> HotelBooking.Infrastructure

HotelBooking.Application
    -> HotelBooking.Domain

HotelBooking.Infrastructure
    -> HotelBooking.Application
    -> HotelBooking.Domain

HotelBooking.Domain
    -> no application-specific external project
```

The API references Infrastructure only at the composition root so it can call
dependency-registration methods. Controllers should depend on Application
handlers or interfaces, not Infrastructure implementations.

Infrastructure references Application because it implements persistence and
external-service interfaces declared by Application. Domain remains independent
of API, EF Core, SQL Server, and deployment concerns.

## Project Responsibilities

### HotelBooking.Api

The API project is the HTTP entry point. It contains:

- Controllers or endpoint definitions.
- HTTP request and response contracts when they are transport-specific.
- Authentication and authorization configuration.
- Swagger/OpenAPI configuration.
- Middleware, filters, and HTTP exception mapping.
- Dependency registration at the application composition root.
- Health endpoints.

Controllers translate HTTP input into Application commands or queries. They do
not contain database queries or booking business rules.

### HotelBooking.Application

The Application project coordinates use cases. It contains:

- Commands and command handlers.
- Queries and query handlers.
- Persistence and external-service interfaces.
- Use-case validation.
- Application result types and read models.
- Transaction orchestration where a use case spans multiple operations.
- Application dependency registration.

Application describes what the use case needs without deciding how SQL Server,
email, queues, or other infrastructure implements it.

### HotelBooking.Domain

The Domain project contains business concepts and rules that remain meaningful
without ASP.NET Core or EF Core. It contains:

- Entities and aggregate roots.
- Value objects.
- Domain enums.
- Domain services when behavior does not belong to one entity.
- Domain exceptions or result types.
- Business invariants and domain events where useful.

For this system, date overlap, room capacity, and booking invariants are Domain
concerns. `BookingRules` would therefore move from Services into Domain, or its
behavior would move into the relevant entities and value objects.

### HotelBooking.Infrastructure

Infrastructure implements technical details. It contains:

- `HotelBookingDbContext`.
- EF Core entity configuration.
- EF Core migrations.
- SQL Server configuration.
- Repository implementations.
- Read-query implementations.
- Implementations for external APIs, storage, messaging, or email.
- Infrastructure dependency registration.

Infrastructure owns EF Core LINQ and provider-specific behavior when the
Application layer is intended to remain independent of EF Core.

## Recommended Command And Query Split

Large applications commonly benefit from separating write-oriented commands
from read-oriented queries without requiring a complicated CQRS platform.

```text
HTTP request
    -> Application command/query handler
    -> Application interface
    -> Infrastructure implementation
    -> EF Core / SQL Server
```

### Commands

Commands change system state:

```text
CreateBookingCommand
CancelBookingCommand
ChangeBookingDatesCommand
```

Command handlers coordinate the use case, load the required domain objects,
apply business rules, and commit one unit of work.

Repositories are most useful for command-side aggregate behavior. Prefer
focused interfaces such as:

```csharp
public interface IBookingRepository
{
    Task<Booking?> GetAsync(
        Guid bookingId,
        CancellationToken cancellationToken);

    void Add(Booking booking);
}
```

Avoid a generic interface that merely repeats `DbSet` operations:

```csharp
// Avoid using this as the default abstraction.
public interface IRepository<TEntity>
{
    IQueryable<TEntity> GetAll();
    void Add(TEntity entity);
    void Delete(TEntity entity);
}
```

Returning `IQueryable` also lets database-query details escape the persistence
boundary and makes it unclear which layer owns query behavior.

EF Core's `DbContext` already provides change tracking and unit-of-work
behavior. A custom repository should express domain or use-case intent rather
than duplicating all of `DbContext`.

### Queries

Queries return data without changing system state:

```text
SearchHotelsQuery
FindAvailableRoomsQuery
GetBookingByReferenceQuery
```

Read queries often work better as focused query interfaces and implementations
than aggregate repositories. They can project directly into read models and do
not need to materialize complete domain aggregates.

## Hotel Search Example

The hotel search components would be placed as follows:

| Component | Project |
|---|---|
| `SearchHotelsQuery` | Application |
| `SearchHotelsQueryHandler` | Application |
| `IHotelQueries` | Application |
| `HotelSummary` | Application |
| `HotelQueries` | Infrastructure |
| `HotelBookingDbContext` | Infrastructure |
| EF Core migrations | Infrastructure |
| `Hotel` entity | Domain |

### Application query

```csharp
namespace HotelBooking.Application.Hotels;

public sealed record SearchHotelsQuery(string? Name);
```

### Application read model

```csharp
namespace HotelBooking.Application.Hotels;

public sealed record HotelSummary(Guid Id, string Name);
```

### Application persistence interface

```csharp
namespace HotelBooking.Application.Hotels;

public interface IHotelQueries
{
    Task<IReadOnlyList<HotelSummary>> SearchAsync(
        string? name,
        CancellationToken cancellationToken);
}
```

### Application query handler

```csharp
namespace HotelBooking.Application.Hotels;

public sealed class SearchHotelsQueryHandler(IHotelQueries hotelQueries)
{
    public Task<IReadOnlyList<HotelSummary>> HandleAsync(
        SearchHotelsQuery query,
        CancellationToken cancellationToken)
    {
        return hotelQueries.SearchAsync(query.Name, cancellationToken);
    }
}
```

The handler owns the use case but does not depend on EF Core or
`HotelBookingDbContext`.

### Infrastructure query implementation

```csharp
using HotelBooking.Application.Hotels;
using HotelBooking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelBooking.Infrastructure.Queries;

public sealed class HotelQueries(
    HotelBookingDbContext dbContext) : IHotelQueries
{
    public async Task<IReadOnlyList<HotelSummary>> SearchAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Hotels.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var normalizedName = name.Trim();

            query = query.Where(hotel =>
                hotel.Name.Contains(normalizedName));
        }

        return await query
            .OrderBy(hotel => hotel.Name)
            .Select(hotel => new HotelSummary(
                hotel.Id,
                hotel.Name))
            .ToListAsync(cancellationToken);
    }
}
```

### API controller

```csharp
using HotelBooking.Application.Hotels;
using Microsoft.AspNetCore.Mvc;

namespace HotelBooking.Api.Controllers;

[ApiController]
[Route("api/hotels")]
public sealed class HotelsController(
    SearchHotelsQueryHandler handler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HotelSummary>>> Search(
        [FromQuery] string? name,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new SearchHotelsQuery(name),
            cancellationToken);

        return Ok(result);
    }
}
```

## Dependency Registration

Application registers handlers and application services:

```csharp
public static IServiceCollection AddHotelBookingApplication(
    this IServiceCollection services)
{
    services.AddScoped<SearchHotelsQueryHandler>();
    services.AddScoped<CreateBookingCommandHandler>();

    return services;
}
```

Infrastructure registers persistence implementations:

```csharp
public static IServiceCollection AddHotelBookingInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddDbContext<HotelBookingDbContext>(options =>
        options.UseSqlServer(
            configuration.GetConnectionString("HotelBooking")));

    services.AddScoped<IHotelQueries, HotelQueries>();
    services.AddScoped<IBookingRepository, BookingRepository>();

    return services;
}
```

The API composition root calls both:

```csharp
builder.Services.AddHotelBookingApplication();
builder.Services.AddHotelBookingInfrastructure(builder.Configuration);
```

## Testing Strategy

### Domain unit tests

Test domain rules without EF Core or ASP.NET Core:

- Date overlap.
- Room capacity.
- Booking date invariants.
- Domain state transitions.

### Application unit tests

Application handlers can use stubs or mocks for focused interfaces such as
`IHotelQueries` or `IBookingRepository`. These tests verify orchestration and
result mapping without attempting to mock EF Core LINQ behavior.

### Infrastructure integration tests

Test EF Core queries, transactions, migrations, constraints, and concurrency
against the real production database engine. For this project, continue using a
disposable SQL Server through Testcontainers rather than relying on EF Core
InMemory.

### API integration tests

Run the complete HTTP flow through `WebApplicationFactory` with the disposable
SQL Server database:

```text
HTTP request
    -> controller
    -> Application handler
    -> Infrastructure query/repository
    -> EF Core
    -> SQL Server
```

### Architecture tests

For a large solution, automated architecture tests can enforce rules such as:

- Domain must not reference Application, Infrastructure, or API.
- Application must not reference Infrastructure or API.
- Infrastructure must not reference API.
- Controllers must not depend directly on `HotelBookingDbContext`.

## When Direct DbContext Use Is Still Reasonable

Large project size alone does not require custom repositories. Directly using
`DbContext` from focused command or query handlers can remain appropriate when:

- The application is intentionally committed to EF Core.
- Handlers remain small and use-case focused.
- Real database integration tests cover query behavior.
- The team does not need persistence test doubles.
- A repository would only repeat EF Core methods.

Choose and document one approach per bounded area. Avoid mixing patterns
randomly, such as some controllers querying `DbContext`, some services using a
generic repository, and other handlers using focused repositories without a
clear reason.

## Incremental Evolution Plan

Do not restructure a working system into four projects in one large rewrite.
Evolve only when there is a concrete need:

1. Keep controllers thin and identify clear commands and queries.
2. Move reusable business rules into a Domain project.
3. Rename or evolve Services into Application as use-case handlers emerge.
4. Move Repository into Infrastructure and keep EF Core details there.
5. Introduce focused persistence interfaces only at boundaries that need them.
6. Add architecture tests to preserve the selected dependency direction.
7. Continue testing database behavior against SQL Server.

Each step should leave the application runnable and testable. The architecture
is successful when it reduces coupling and clarifies ownership, not merely when
it contains more projects and interfaces.

## Recommendation For This Challenge

Do not apply this restructuring to the current challenge submission. The
existing architecture is proportionate:

```text
HotelBooking.Api
HotelBooking.Models
HotelBooking.Repository
HotelBooking.Services
```

Direct `DbContext` queries inside focused services are acceptable here because
the use cases are small, the SQL behavior is covered by Testcontainers-backed
integration tests, and an additional abstraction would add more code than
value. Use this document as an explanation of how the design could evolve in a
larger production system.

## References

- [Microsoft: Common web application architectures](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [Microsoft: Designing the infrastructure persistence layer](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design)
- [Microsoft: Choosing an EF Core testing strategy](https://learn.microsoft.com/ef/core/testing/choosing-a-testing-strategy)
- [Microsoft: DbContext lifetime and unit of work](https://learn.microsoft.com/ef/core/dbcontext-configuration/)
