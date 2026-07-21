# Solution Architecture

This diagram shows the runtime flow through the current solution projects and
the local and Azure database targets.

```mermaid
flowchart TB
    Client["API Consumer<br/>Swagger / HTTP Client"]

    subgraph Api["HotelBooking.Api"]
        Program["Program.cs<br/>Dependency registration"]
        Controllers["REST Controllers<br/>Hotels · Bookings · Admin"]
        Docs["Swagger / OpenAPI"]
        Health["Health Check"]
    end

    subgraph Services["HotelBooking.Services"]
        HotelSearch["Hotel Search Service"]
        Availability["Room Availability Service"]
        Booking["Booking Service"]
        TestData["Seed / Reset Service"]
        Rules["Booking Rules<br/>Dates · Capacity · Overlap"]
    end

    subgraph Repository["HotelBooking.Repository"]
        DbContext["HotelBookingDbContext"]
        Configuration["EF Core Configuration<br/>Keys · Indexes · Constraints"]
        Migrations["EF Core Migrations"]
    end

    subgraph Models["HotelBooking.Models"]
        Entities["Hotel · Room · Booking · RoomType"]
    end

    subgraph Database["SQL Server"]
        LocalDb["Local SQL Server<br/>Docker Compose"]
        AzureDb["Azure SQL Database"]
    end

    Client --> Controllers
    Client --> Docs
    Client --> Health

    Program --> Controllers
    Program --> Services
    Program --> Repository

    Controllers --> HotelSearch
    Controllers --> Availability
    Controllers --> Booking
    Controllers --> TestData

    HotelSearch --> DbContext
    Availability --> DbContext
    Booking --> Availability
    Booking --> DbContext
    TestData --> DbContext

    HotelSearch --> Rules
    Availability --> Rules
    Booking --> Rules

    DbContext --> Configuration
    Configuration --> Entities
    DbContext --> LocalDb
    DbContext --> AzureDb
    Migrations -. "applied by MigrateAsync" .-> LocalDb
    Migrations -. "applied by MigrateAsync" .-> AzureDb

    Controllers -. "request / response DTO mapping" .-> Entities
    Services -. "uses domain models" .-> Entities
```

The API project is the HTTP entry point, Services owns the booking use cases,
Repository configures EF Core and SQL Server persistence, and Models contains
the shared data model.
