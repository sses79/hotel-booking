# Azure Infrastructure Architecture

This diagram shows the source-to-deployment flow and the Azure resources
provisioned for the hosted API.

```mermaid
flowchart TB
    User["API consumer<br/>Swagger / HTTP client"]
    Operator["Developer / deployment operator<br/>Azure CLI"]

    subgraph GitHub["GitHub"]
        Repository["hotel-booking repository"]
        Actions["GitHub Actions<br/>build API image on main"]
        GHCR["GitHub Container Registry<br/>hotel-booking-api:&lt;commit-sha&gt;"]
    end

    Bicep["infra/bicep/main.bicep"]

    subgraph Azure["Azure resource group"]
        subgraph ContainerApps["Azure Container Apps Consumption environment"]
            Ingress["External HTTPS ingress"]

            subgraph ApiApp["Hotel Booking API Container App<br/>single active revision · scale 0–2"]
                Api["HotelBooking.Api<br/>Controllers · Swagger · Health"]
                Services["HotelBooking.Services<br/>Search · Availability · Booking · Seed/Reset"]
                Persistence["HotelBooking.Repository<br/>EF Core DbContext · Migrations"]
                Models["HotelBooking.Models<br/>Hotel · Room · Booking · RoomType"]
                Secret["Container App secret<br/>SQL connection string"]

                Api --> Services
                Services --> Persistence
                Services -. "domain types" .-> Models
                Persistence -. "entity mappings" .-> Models
                Secret --> Persistence
            end
        end

        subgraph SqlPlatform["Azure SQL logical server"]
            Firewall["Firewall rule<br/>Allow Azure services"]
            Entra["Microsoft Entra administrator"]
            SqlDb["HotelBooking database<br/>General Purpose serverless<br/>0.5–2 vCores · auto-pause 60 min"]

            Firewall --> SqlDb
            Entra -. "administration" .-> SqlDb
        end
    end

    Repository --> Actions
    Actions -->|"push immutable SHA image"| GHCR
    Repository --> Bicep
    Operator -->|"az deployment group create"| Bicep
    Bicep -. "provision and configure" .-> Ingress
    Bicep -. "provision and configure" .-> SqlDb
    GHCR -->|"anonymous image pull"| Api
    User -->|"HTTPS"| Ingress
    Ingress --> Api
    Persistence -->|"encrypted SQL connection · TCP 1433"| SqlDb
```

GitHub Actions publishes an immutable commit-SHA image to GHCR. Bicep
provisions the Container Apps environment, externally accessible API, SQL
logical server, firewall rule, Entra administrator, and serverless database.
